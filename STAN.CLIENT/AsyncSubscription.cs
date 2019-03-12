﻿// Copyright 2015-2018 The NATS Authors
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
using NATS.Client;
using System;
using System.Threading;

namespace STAN.Client
{
    class AsyncSubscription : IStanSubscription
    {
        // fields

        private StanSubscriptionOptions _options;
        private string _subject;
        private Connection _conn;
        private string _ackInbox;
        private IAsyncSubscription _inboxSub;
        private volatile bool _disposed;

        // constructors

        internal AsyncSubscription(Connection conn, StanSubscriptionOptions opts)
        {
            // TODO: Complete member initialization
            _options = StanSubscriptionOptions.GetFrom(opts);
            _conn = conn;
            Inbox = Connection.NewInbox();
        }

        // auxiliary properties and methods

        internal static StanSubscriptionOptions DefaultOptions => StanSubscriptionOptions.GetDefaultOptions();

        internal string Inbox { get; }

        private bool IsDurable => !string.IsNullOrEmpty(_options.DurableName);

        private long ConvertTimeSpan(TimeSpan ts) => ts.Ticks * 100;

        private void Ack(MsgProto msg) => _conn.NatsConn.Publish(_ackInbox, ProtocolSerializer.CreateAck(msg));

        internal void ManualAck(StanMsg m)
        {
            if (!_options.ManualAcks)
            {
                throw new StanManualAckException();
            }

            Ack(m.Proto);
        }

        // in STAN, much of this code is in the connection module.
        internal void Subscribe(string subRequestSubject, string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler)
        {
            _subject = subject;

            try
            {
                // Listen for actual messages.
                _inboxSub = _conn.NatsConn.SubscribeAsync(Inbox, (sender, args) =>
                {
                    if (!_disposed)
                    {
                        var msg = new MsgProto();
                        ProtocolSerializer.Unmarshal(args.Message.Data, msg);

                        handler(this, new StanMsgHandlerArgs(new StanMsg(msg, this)));

                        if (!_options.ManualAcks)
                        {
                            try
                            {
                                Ack(msg);
                            }
                            catch (Exception)
                            {
                                /*
                                 * Ignore - subscriber could have closed the connection 
                                 * or there's been a connection error.  The server will 
                                 * resend the unacknowledged messages.
                                 */
                            }
                        }
                    }
                });

                var sr = new SubscriptionRequest
                {
                    ClientID = _conn.ClientID,
                    Subject = subject,
                    QGroup = qgroup ?? string.Empty,
                    Inbox = Inbox,
                    MaxInFlight = _options.MaxInflight,
                    AckWaitInSecs = _options.AckTimeout / 1000,
                    StartPosition = _options.StartPosition,
                    DurableName = _options.DurableName,
                };

                // Conditionals
                switch (sr.StartPosition)
                {
                    case StartPosition.TimeDeltaStart:
                        sr.StartTimeDelta = ConvertTimeSpan(_options.UseStartTimeDelta ? _options.StartTimeDelta : (DateTime.UtcNow - _options.StartTime));
                        break;
                    case StartPosition.SequenceStart:
                        sr.StartSequence = _options.StartSequence;
                        break;
                }

                byte[] b = ProtocolSerializer.Marshal(sr);

                // TODO: Configure request timeout?
                Msg m = _conn.NatsConn.Request(subRequestSubject, b, 2000);

                var r = new SubscriptionResponse();
                ProtocolSerializer.Unmarshal(m.Data, r);
                if (!string.IsNullOrWhiteSpace(r.Error))
                {
                    throw new StanException(r.Error);
                }

                _ackInbox = r.AckInbox;
            }
            catch (Exception e)
            {
                _inboxSub?.Dispose();
                throw e is NATSConnectionClosedException ? new StanConnectionClosedException(e) : e;
            }
        }

        private void Dispose(bool disposing, bool close, bool throwEx)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    // Dispose all managed resources.

                    try
                    {
                        // handles both the subscripition unsubscribe and close operations.
                        _inboxSub.Unsubscribe();
                        _conn.Unsubscribe(_subject, _inboxSub.Subject, _ackInbox, close);
                    }
                    catch
                    {
                        if (throwEx)
                            throw;
                    }

                    GC.SuppressFinalize(this);
                }
                // Clean up unmanaged resources here.
            }
        }

        // public api

        public void Unsubscribe() => Dispose(true, false, true);

        public void Close() => Dispose(true, true, true);

        public void Dispose() => Dispose(true, IsDurable && _options.LeaveOpen, false);
    }
}
