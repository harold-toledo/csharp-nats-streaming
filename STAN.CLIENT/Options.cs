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
using NATS.Client;

namespace STAN.Client
{
    /// <summary>
    /// Options available to configure a connection to the NATS streaming server.
    /// </summary>
    public sealed class StanOptions
    {
        private string _natsURL = StanConsts.DefaultNatsURL;
        private int _connectTimeout = StanConsts.DefaultConnectWait;
        private long _ackTimeout = StanConsts.DefaultConnectWait;
        private string _discoverPrefix = StanConsts.DefaultDiscoverPrefix;
        private long _maxPubAcksInflight = StanConsts.DefaultMaxPubAcksInflight;
        private int _pingMaxOut = StanConsts.DefaultPingMaxOut;
        private int _pingInterval = StanConsts.DefaultPingInterval;

        private StanOptions() { }

        private StanOptions(StanOptions opts)
        {
            if (opts != null)
            {
                NatsURL = opts.NatsURL;
                NatsConn = opts.NatsConn;
                ConnectTimeout = opts.ConnectTimeout;
                PubAckWait = opts.PubAckWait;
                DiscoverPrefix = opts.DiscoverPrefix;
                MaxPubAcksInFlight = opts.MaxPubAcksInFlight;
                PingInterval = opts.PingInterval;
                PingMaxOutstanding = opts.PingMaxOutstanding;
            }
        }

        /// <summary>
        /// Gets or sets the url to connect to a NATS server.
        /// </summary>
	    public string NatsURL
        {
            get
            {
                return _natsURL;
            }
            set
            {
                _natsURL = string.IsNullOrWhiteSpace(value) ? StanConsts.DefaultNatsURL : value;
            }
        }

        /// <summary>
        /// Sets the underlying NATS connection to be used by a NATS Streaming 
        /// connection object.
        /// </summary>
        public IConnection NatsConn { internal get; set; }

        /// <summary>
        /// ConnectTimeout is an option to set the timeout (in milliseconds) for establishing a connection.
        /// The value must be greater than zero.
        /// </summary>
        public int ConnectTimeout
        {
            get
            {
                return _connectTimeout;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "ConnectTimeout must be greater than zero.");

                _connectTimeout = value;
            }
        }

        /// <summary>
        /// PubAckWait is an option to set the timeout (in milliseconds) for waiting for an ACK
        /// for a published message. The value must be greater than zero.
        /// </summary>
        public long PubAckWait
        {
            get
            {
                return _ackTimeout;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "PubAckWait must be greater than zero.");

                _ackTimeout = value;
            }
        }

        /// <summary>
        /// Sets the discover prefix used in connecting to the NATS streaming server.
        /// This must match the settings on the NATS streaming server.
        /// </summary>
        public string DiscoverPrefix
        {
            get
            {
                return _discoverPrefix;
            }
            set
            {
                _discoverPrefix = value ?? throw new ArgumentNullException(nameof(value), "DiscoverPrefix cannot be null.");
            }
        }

        /// <summary>
        /// MaxPubAcksInflight is an option to set the maximum number 
        /// of published messages with outstanding ACKs from the server.
        /// The value must be greater than zero.
        /// </summary>
        public long MaxPubAcksInFlight
        {
            get
            {
                return _maxPubAcksInflight;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "MaxPubAcksInFlight must be greater than zero.");

                _maxPubAcksInflight = value;
            }
        }

        /// <summary>
        /// MaxPingsOut is an option to set the maximum number 
        /// of outstanding pings with the streaming server.
        /// The value must be greater than two.
        /// See <see cref="StanConsts.DefaultPingMaxOut"/>.
        /// </summary>
        public int PingMaxOutstanding
        {
            get
            {
                return _pingMaxOut;
            }
            set
            {
                if (value <= 2)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "PingMaxOutstanding must be greater than two.");

                _pingMaxOut = value;
            }
        }

        /// <summary>
        /// PingInterval is an option to set the interval of pings in milliseconds.
        /// The value must be greater than zero.
        /// See <see cref="StanConsts.DefaultPingInterval"/>.
        /// </summary>
        public int PingInterval
        {
            get
            {
                return _pingInterval;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "PingInterval must be greater than zero.");

                _pingInterval = value;
            }
        }

        /// <summary>
        /// Returns the default connection options.
        /// </summary>
        public static StanOptions GetDefaultOptions() => new StanOptions();

        /// <summary>
        /// Returns new connection options using available values from the provided options. 
        /// Default values will be used for values not available in the provided options.
        /// </summary>
        public static StanOptions GetFrom(StanOptions opts) => new StanOptions(opts);
    }
}
