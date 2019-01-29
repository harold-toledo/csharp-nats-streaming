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
        private string natsURL = StanConsts.DefaultNatsURL;
        private int connectTimeout = StanConsts.DefaultConnectWait;
        private long ackTimeout = StanConsts.DefaultConnectWait;
        private string discoverPrefix = StanConsts.DefaultDiscoverPrefix;
        private long maxPubAcksInflight = StanConsts.DefaultMaxPubAcksInflight;
        private int pingMaxOut = StanConsts.DefaultPingMaxOut;
        private int pingInterval = StanConsts.DefaultPingInterval;

        internal static string DeepCopy(string value)
        {
            if (value == null)
                return null;

            return new string(value.ToCharArray());
        }

        private StanOptions() { }

        private StanOptions(StanOptions opts)
        {
            if (opts != null)
            {
                ackTimeout = opts.ackTimeout;
                NatsURL = DeepCopy(opts.NatsURL);
                ConnectTimeout = opts.ConnectTimeout;
                PubAckWait = opts.PubAckWait;
                DiscoverPrefix = DeepCopy(opts.DiscoverPrefix);
                MaxPubAcksInFlight = opts.MaxPubAcksInFlight;
                NatsConn = opts.NatsConn;
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
                return natsURL;
            }
            set
            {
                natsURL = value ?? StanConsts.DefaultNatsURL;
            }
        }

        /// <summary>
        /// Sets the underlying NATS connection to be used by a NATS Streaming 
        /// connection object.
        /// </summary>
        public IConnection NatsConn { internal get; set; }

        /// <summary>
        /// ConnectTimeout is an Option to set the timeout (in milliseconds) for establishing a connection.
        /// </summary>
        public int ConnectTimeout
        {
            get
            {
                return connectTimeout;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "ConnectTimeout must be greater than zero.");

                connectTimeout = value;
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
                return ackTimeout;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "PubAckWait must be greater than zero.");

                ackTimeout = value;
            }
        }

        /// <summary>
        /// Sets the discover prefix used in connecting to the NATS streaming server.
        /// This must match the settings on the NATS streaming sever.
        /// </summary>
        public string DiscoverPrefix
        {
            get
            {
                return discoverPrefix;
            }
            set
            {
                discoverPrefix = value ?? throw new ArgumentNullException("value", "DiscoverPrefix cannot be null.");
            }
        }

        /// <summary>
        /// MaxPubAcksInflight is an Option to set the maximum number 
        /// of published messages without outstanding ACKs from the server.
        /// </summary>
        public long MaxPubAcksInFlight
        {
            get
            {
                return maxPubAcksInflight;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "MaxPubAcksInFlight must be greater than zero.");

                maxPubAcksInflight = value;
            }
        }

        /// <summary>
        /// MaxPingsOut is an option to set the maximum number 
        /// of outstanding pings with the streaming server.
        /// See <see cref="StanConsts.DefaultPingMaxOut"/>.
        /// </summary>
        public int PingMaxOutstanding
        {
            get
            {
                return pingMaxOut;
            }
            set
            {
                if (value <= 2)
                    throw new ArgumentOutOfRangeException("value", value, "PingMaxOutstanding must be greater than two.");

                pingMaxOut = value;
            }
        }

        /// <summary>
        /// PingInterval is an option to set the interval of pings in milliseconds.
        /// See <see cref="StanConsts.DefaultPingInterval"/>.
        /// </summary>
        public int PingInterval
        {
            get
            {
                return pingInterval;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "PingInterval must be greater than zero.");

                pingInterval = value;
            }
        }

        /// <summary>
        /// Returns the default connection options.
        /// </summary>
        public static StanOptions GetDefaultOptions() => new StanOptions();

        /// <summary>
        /// Returns the default connection options.
        /// </summary>
        public static StanOptions GetFrom(StanOptions opts) => new StanOptions(opts);
    }
}
