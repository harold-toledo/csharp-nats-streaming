// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client;
using Google.Protobuf;

/*! \mainpage %NATS .NET Streaming Client.
 *
 * \section intro_sec Introduction
 *
 * The %NATS .NET Streaming Client is part of %NATS an open-source, cloud-native
 * messaging system.
 * This client, written in C#, follows the go client closely, but
 * diverges in places to follow the common design semantics of a .NET API.
 *
 * \section install_sec Installation
 *
 * Instructions to build and install the %NATS .NET C# Streaming Client can be
 * found at the [NATS .NET C# Streaming Client GitHub page](https://github.com/nats-io/csharp-nats-streaming)
 *
 * \section other_doc_section Other Documentation
 *
 * This documentation focuses on the %NATS .NET C# Streaming Client API; for additional
 * information, refer to the following:
 *
 * - [General Documentation for nats.io](http://nats.io/documentation)
 * - [NATS .NET C# Streaming Client found on GitHub](https://github.com/nats-io/csharp-nats-streaming)
 * - [NATS .NET C# Client found on GitHub](https://github.com/nats-io/csnats)
 * - [The NATS server (gnatsd) found on GitHub](https://github.com/nats-io/gnatsd)
 */

// disable XML comment warnings
#pragma warning disable 1591

namespace STAN.Client
{
    internal class PublishAck
    {
        // fields

        private volatile bool _completed;

        private Connection _conn;
        private EventHandler<StanAckHandlerArgs> _handler;
        private TimeSpan _timeout;
        private Timer _timer;
        private Exception _ex;

        // constructors

        internal PublishAck(Connection conn, string guid, EventHandler<StanAckHandlerArgs> handler, long timeout)
        {
            GUID = guid;
            _conn = conn;
            _handler = handler;
            _timeout = TimeSpan.FromMilliseconds(timeout);
            _timer = new Timer(AckTimeout, null, Timeout.Infinite, Timeout.Infinite);
        }

        // auxiliary properties and methods

        private void AckTimeout(object state) => _conn.HandleAck(GUID, "Timeout occurred.", true);

        // public api

        public string GUID { get; }

        public void StartTimeoutMonitor()
        {
            try
            {
                _timer.Change(_timeout, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // There is no need to handle or even log this exception.
                // The only reason for this to happen is if we received
                // an ACK before being able to start the timeout timer.
                // At this point Complete(string error, bool dataPublished) 
                // has been called and the timer disposed. There is no need 
                // to start the timer to check for a timeout.
            }
        }

        public void Wait()
        {
            SpinWait.SpinUntil(() => _completed);

            if (_ex != null) throw _ex;
        }

        public void Complete(string error, bool dataPublished)
        {
            if (!_completed)
            {
                _timer.Dispose();

                if (dataPublished)
                {
                    error = error?.Trim();

                    if (_handler != null)
                    {
                        try
                        {
                            _handler(this, new StanAckHandlerArgs(GUID, error));
                        }
                        catch { /* ignore user exceptions */ }
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        _ex = new StanException(error);
                    }
                }

                _completed = true;
            }
        }
    }

    public class Connection : IStanConnection, IDisposable
    {
        // fields

        private const string _pingsFailure = "Connection lost due to PING failure.";

        private static List<string> _connectionFailurePatterns = new List<string>
        {
            "^stan: invalid publish request",
            "^Connection closed.",
            "^Connection is closed.",
            "^Connection is stale.",
            $"^{_pingsFailure}",
        };

        private readonly object _lock = new object();
        private readonly CancellationTokenSource _tokenSource;
        private readonly CancellationToken _token;

        private volatile bool _disposed;

        private readonly ByteString _connId;        // This is a NUID that uniquely identifies a connection. Stored as a protobuf ByteString.
        private readonly string _pubPrefix;         // Publish prefix set by stan, append our subject.
        private readonly string _subRequests;       // Subject to send subscription requests.
        private readonly string _unsubRequests;     // Subject to send unsubscribe requests.
        private readonly string _subCloseRequests;  // Subject to send subscrption close requests.
        private readonly string _closeRequests;     // Subject to send close requests.
        private readonly string _ackSubject;        // Subject to which the server needs to send publish acks.
        private readonly string _pingRequests;      // Subject to send the pings.

        private ISubscription _ackSubscription;
        private ISubscription _hbSubscription;
        private ISubscription _pingSubscription;
        private volatile int _pingsWithoutAck;
        private int _pingsFailureNotified;

        private Dictionary<string, AsyncSubscription> _subs;
        private Dictionary<string, PublishAck> _pubACKs;

        // constructors

        private Connection() { }

        internal Connection(string clusterID, string clientID, StanOptions options)
        {
            ClientID = clientID;
            Options = StanOptions.GetFrom(options);

            _connId = ByteString.CopyFrom(Encoding.UTF8.GetBytes(NewGUID()));

            if (IsNatsConnOwned)
            {
                try
                {
                    NatsConn = new ConnectionFactory().CreateConnection(Options.NatsURL);
                }
                catch (Exception e)
                {
                    throw new StanConnectionException(e);
                }
            }
            else
            {
                NatsConn = Options.NatsConn;
            }

            // create a heartbeats inbox
            string hbInbox = NewInbox();
            _hbSubscription = NatsConn.SubscribeAsync(hbInbox, ProcessHeartBeat);

            var resp = new ConnectResponse();
            try
            {
                // The streaming server expects seconds, but can handle milliseconds as well.
                // Milliseconds are denoted by negative numbers.
                int pingInterval = Options.PingInterval < 1000 ? Options.PingInterval * -1 : Options.PingInterval / 1000;

                byte[] data = ProtocolSerializer.Marshal(new ConnectRequest
                {
                    ClientID = ClientID,
                    HeartbeatInbox = hbInbox,
                    ConnID = _connId,
                    Protocol = StanConsts.ProtocolOne,
                    PingMaxOut = Options.PingMaxOutstanding,
                    PingInterval = pingInterval,
                });

                Msg cr = NatsConn.Request($"{Options.DiscoverPrefix}.{clusterID}", data, Options.ConnectTimeout);

                ProtocolSerializer.Unmarshal(cr.Data, resp);
            }
            catch (NATSTimeoutException)
            {
                throw new StanConnectRequestTimeoutException();
            }
            catch (Exception e)
            {
                throw new StanConnectRequestException(e);
            }

            if (!string.IsNullOrWhiteSpace(resp.Error))
            {
                throw new StanConnectRequestException(resp.Error);
            }

            // capture cluster configuration endpoints to publish and subscribe/unsubscribe
            _pubPrefix = resp.PubPrefix;
            _subRequests = resp.SubRequests;
            _unsubRequests = resp.UnsubRequests;
            _subCloseRequests = resp.SubCloseRequests;
            _closeRequests = resp.CloseRequests;
            _pingRequests = resp.PingRequests;

            // setup the Ack subscription
            _ackSubject = $"{StanConsts.DefaultACKPrefix}.{NewGUID()}";
            _ackSubscription = NatsConn.SubscribeAsync(_ackSubject, ProcessAck);

            // TODO:  hardcode or options?
            _ackSubscription.SetPendingLimits(1024 * 1024, 32 * 1024 * 1024);

            _subs = new Dictionary<string, AsyncSubscription>();
            _pubACKs = new Dictionary<string, PublishAck>();

            if (resp.Protocol >= StanConsts.ProtocolOne && resp.PingInterval != 0)
            {
                _tokenSource = new CancellationTokenSource();
                _token = _tokenSource.Token;

                // If negative, the value represents milliseconds.
                // If positive, the value represents seconds, but in the .NET clients we always use milliseconds.
                Options.PingInterval = resp.PingInterval < 0 ? resp.PingInterval * -1 : resp.PingInterval * 1000;
                Options.PingMaxOutstanding = resp.PingMaxOut;

                _pingSubscription = NatsConn.SubscribeAsync(NewInbox(), (sender, e) => 
                {
                    // No data means everything is OK (no need to unmarshall)
                    var data = e.Message.Data;
                    if (data?.Length > 0)
                    {
                        var pingResp = new PingResponse();
                        try
                        {
                            ProtocolSerializer.Unmarshal(data, pingResp);
                        }
                        catch
                        {
                            return; // Ignore, this as an invalid protocol message.
                        }
                        string err = pingResp.Error?.Trim() ?? string.Empty;
                        if (err.Length > 0)
                        {
                            Dispose();
                            PingsFailure(err);
                        }
                    }
                    _pingsWithoutAck = 0;
                });

                // starting ping-like functionality
                var task = Task.Run(async () =>
                {
                    byte[] ping = ProtocolSerializer.CreatePing(_connId);
                    var pingsInterval = TimeSpan.FromMilliseconds(Options.PingInterval);
                    string err = string.Empty;

                    bool IsConnectionFailure(Exception e) =>
                        e is StanConnectionClosedException ||
                        e is NATSConnectionClosedException ||
                        e is NATSStaleConnectionException ||
                        _connectionFailurePatterns.Any(pattern => Regex.IsMatch(e.Message, pattern, RegexOptions.IgnoreCase));

                    while (!_token.IsCancellationRequested && err.Length == 0)
                    {
                        await Task.Delay(pingsInterval);

                        if (Interlocked.Increment(ref _pingsWithoutAck) > Options.PingMaxOutstanding)
                        {
                            err = _pingsFailure;
                        }
                        else
                        {
                            try
                            {
                                NatsConn.Publish(_pingRequests, _pingSubscription.Subject, ping);
                            }
                            catch (Exception e)
                            {
                                err = IsConnectionFailure(e) ? e.Message : err;
                            }
                        }
                    }

                    _token.ThrowIfCancellationRequested();

                    // If we are here, a connection failure has occurred.
                    Dispose();
                    PingsFailure(err);

                }, _token);
            }
        }

        // auxiliary propertites and methods

        private bool IsNatsConnOwned => Options.NatsConn == null;

        private void PingsFailure(string details)
        {
            // Making sure that if a connection lost handler was specified, it will be called only once.
            if (Interlocked.CompareExchange(ref _pingsFailureNotified, 1, 0) == 0)
            {
                try
                {
                    Options.ConnectionLostHandler?.Invoke(details);
                }
                catch { /* ignore user exceptions */ }
            }
        }

        private void ProcessHeartBeat(object sender, MsgHandlerEventArgs args) => NatsConn.Publish(args.Message.Reply, null);

        private PublishAck RemoveAck(string guid)
        {
            PublishAck ack;

            lock (_lock)
            {
                if (_pubACKs.TryGetValue(guid, out ack))
                {
                    _pubACKs.Remove(guid);
                    Monitor.Pulse(_lock);
                }
            }

            return ack;
        }

        internal void HandleAck(string guid, string error, bool dataPublished) => RemoveAck(guid)?.Complete(error, dataPublished);

        private void ProcessAck(object sender, MsgHandlerEventArgs args)
        {
            var ack = new PubAck();

            try
            {
                ProtocolSerializer.Unmarshal(args.Message.Data, ack);
            }
            catch (Exception)
            {
                // TODO:  (cls) handle this...
                return;
            }

            HandleAck(ack.Guid, ack.Error, true);
        }

        public IConnection NatsConn { get; }

        private bool IsClosed => NatsConn.IsClosed();

        private void ThrowIfDisposed()
        {
            if (_disposed || IsClosed)
            {
                throw new StanConnectionClosedException();
            }
        }

        private static string NewGUID() => NUID.NextGlobal;

        private PublishAck publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler)
        {
            ThrowIfDisposed();

            string subj = $"{_pubPrefix}.{subject}";
            string guid = NewGUID();
            byte[] b = ProtocolSerializer.CreatePubMsg(ClientID, guid, subject, data, _connId);

            var ack = new PublishAck(this, guid, handler, Options.PubAckTimeout);

            lock (_lock)
            {
                while (_pubACKs.Count >= Options.MaxPubAcksInFlight)
                {
                    Monitor.Wait(_lock);
                }
                _pubACKs[ack.GUID] = ack;
            }

            try
            {
                NatsConn.Publish(subj, _ackSubject, b);
            }
            catch (Exception e)
            {
                HandleAck(guid, null, false);
                throw e is NATSConnectionClosedException ? new StanConnectionClosedException(e) : e;
            }

            ack.StartTimeoutMonitor();

            return ack;
        }

        public void Publish(string subject, byte[] data) => publish(subject, data, null).Wait();

        public string Publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler) => publish(subject, data, handler).GUID;

        public Task<string> PublishAsync(string subject, byte[] data)
        {
            var ack = publish(subject, data, null);

            var t = new Task<string>(() =>
            {
                ack.Wait();
                return ack.GUID;
            });
            t.Start();

            return t;
        }

        private IStanSubscription Subscribe(string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler, StanSubscriptionOptions options)
        {
            ThrowIfDisposed();

            var sub = new AsyncSubscription(this, options);

            sub.Subscribe(_subRequests, subject, qgroup, handler);

            lock (_lock)
            {
                // Register the subscription
                _subs[sub.Inbox] = sub;
            }

            return sub;
        }

        internal void Unsubscribe(string subject, string inbox, string ackInbox, bool close)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                _subs.Remove(inbox);
            }

            string requestSubject = _unsubRequests;
            if (close)
            {
                if (string.IsNullOrEmpty(_subCloseRequests))
                {
                    throw new StanNoServerSupport();
                }
                requestSubject = _subCloseRequests;
            }

            byte[] b = ProtocolSerializer.Marshal(new UnsubscribeRequest
            {
                ClientID = ClientID,
                Subject = subject,
                Inbox = ackInbox,
            });

            var r = NatsConn.Request(requestSubject, b, 2000);
            var sr = new SubscriptionResponse();
            ProtocolSerializer.Unmarshal(r.Data, sr);
            if (!string.IsNullOrEmpty(sr.Error))
                throw new StanException(sr.Error);
        }

        internal static string NewInbox() => $"_INBOX.{NewGUID()}";

        public IStanSubscription Subscribe(string subject, EventHandler<StanMsgHandlerArgs> handler) => 
            Subscribe(subject, AsyncSubscription.DefaultOptions, handler);

        public IStanSubscription Subscribe(string subject, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return Subscribe(subject, null, handler, options);
        }

        public IStanSubscription Subscribe(string subject, string qgroup,EventHandler<StanMsgHandlerArgs> handler) =>
            Subscribe(subject, qgroup, AsyncSubscription.DefaultOptions, handler);

        public IStanSubscription Subscribe(string subject, string qgroup, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));
            if (qgroup == null)
                throw new ArgumentNullException(nameof(qgroup));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return Subscribe(subject, qgroup, handler, options);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                _tokenSource?.Cancel();

                if (!IsClosed)
                {
                    // Dispose all managed resources.

                    void Unsubscribe(ISubscription sub)
                    {
                        try
                        {
                            sub?.Unsubscribe();
                        }
                        catch { /* ignore */ }
                    }

                    lock (_lock)
                    {
                        Unsubscribe(_ackSubscription);
                        Unsubscribe(_hbSubscription);
                        Unsubscribe(_pingSubscription);

                        if (_closeRequests != null)
                        {
                            try
                            {
                                var data = ProtocolSerializer.Marshal(new CloseRequest { ClientID = ClientID });
                                Msg reply = NatsConn.Request(_closeRequests, data, Options.CloseTimeout);
                                // Processing of the response is not needed, but keeping this for reference.
                                if (reply != null)
                                {
                                    var resp = new CloseResponse();
                                    try
                                    {
                                        ProtocolSerializer.Unmarshal(reply.Data, resp);
                                    }
                                    catch (Exception e)
                                    {
                                        throw new StanCloseRequestException(e);
                                    }

                                    if (!string.IsNullOrEmpty(resp.Error))
                                    {
                                        // do not throw exception, consider loging instead
                                    }
                                }
                            }
                            catch {  /* ignore */ }
                        }

                        if (IsNatsConnOwned)
                        {
                            NatsConn.Dispose();
                        }
                    }
                }

                GC.SuppressFinalize(this);
            }
        }

        public void Close() => Dispose();

        public string ClientID { get; }

        public StanOptions Options { get; private set; }
    }
}