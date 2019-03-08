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

namespace STAN.Client
{
    /// <summary>
    /// The StanSubsciption options class represents various options available
    /// to configure a subscription to a subject on the NATS streaming server.
    /// </summary>
    public class StanSubscriptionOptions
    {
        private string _durableName = string.Empty;
        private int _ackWait = 30000;
        private int _maxInflight = StanConsts.DefaultMaxInflight;

        private StanSubscriptionOptions() { }

        private StanSubscriptionOptions(StanSubscriptionOptions opts)
        {
            StartPosition = StartPosition.NewOnly;

            if (opts == null) return;

            DurableName = opts.DurableName;
            AckWait = opts.AckWait;
            LeaveOpen = opts.LeaveOpen;
            ManualAcks = opts.ManualAcks;
            MaxInflight = opts.MaxInflight;
            StartPosition = opts.StartPosition;
            StartSequence = opts.StartSequence;
            UseStartTimeDelta = opts.UseStartTimeDelta;
            StartTime = opts.StartTime;
            StartTimeDelta = opts.StartTimeDelta;
        }

        internal StartPosition StartPosition { get; set; }

        internal ulong StartSequence { get; set; }

        internal DateTime StartTime { get; set; }

        internal bool UseStartTimeDelta { get; set; }

        internal TimeSpan StartTimeDelta { get; set; }

        /// <summary>
        /// DurableName, if set will survive client restarts.
        /// </summary>
        public string DurableName
        {
            get { return _durableName; }
            set
            {
                _durableName = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
            }
        }

        /// <summary>
        /// Do Close() on Disposing subscription if true, or Unsubscribe(). If you want to resume subscription with durable name, set true.
        /// </summary>
        /// <remarks>
        /// If Close() or Unsubscribe() is called before Disposing, this flag has no effect
        /// </remarks>
        public bool LeaveOpen { get; set; }

        public void Durable(string name, bool leaveOpen)
        {
            DurableName = name;
            LeaveOpen = leaveOpen;
        }

        /// <summary>
        /// Controls the number of messages the cluster will have inflight without an ACK.
        /// The value must be greater than zero.
        /// </summary>
        public int MaxInflight
        {
            get { return _maxInflight; }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "MaxInflight must be greater than 0.");

                _maxInflight = value;
            }
        }

        /// <summary>
        /// Controls the time the cluster will wait for an ACK for a given message in milliseconds.
        /// </summary>
        /// <remarks>
        /// The value must be at least one second (1000 ms).
        /// </remarks>
        public int AckWait
        {
            get { return _ackWait; }
            set
            {
                if (value < 1000)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "AckWait cannot be less than 1000.");
 
                _ackWait = value;
            }
        }

        /// <summary>
        /// Controls the time the cluster will wait for an ACK for a given message.
        /// </summary>
        public bool ManualAcks { get; set; }

        /// <summary>
        /// Optional start sequence number.
        /// </summary>
        /// <param name="sequence"></param>
        public void StartAt(ulong sequence)
        {
            StartPosition = StartPosition.SequenceStart;
            StartSequence = sequence;    
        }

        /// <summary>
        /// Optional start time. UTC is recommended although a local time will be converted to UTC.
        /// </summary>
        /// <param name="time"></param>
        public void StartAt(DateTime time)
        {
            StartPosition = StartPosition.TimeDeltaStart;
            UseStartTimeDelta = false;
            StartTime = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        }

        /// <summary>
        /// Optional start at time delta.
        /// </summary>
        /// <param name="duration"></param>
        public void StartAt(TimeSpan duration)
        {
            StartPosition = StartPosition.TimeDeltaStart;
            UseStartTimeDelta = true;
            StartTimeDelta = duration;
        }

        /// <summary>
        /// Start with the last received message.
        /// </summary>
        public void StartWithLastReceived() => StartPosition = StartPosition.LastReceived;
        
        /// <summary>
        /// Deliver all messages available.
        /// </summary>
        public void DeliverAllAvailable() => StartPosition = StartPosition.First;

        /// <summary>
        /// Returns a copy of the default subscription options.
        /// </summary>
        public static StanSubscriptionOptions GetDefaultOptions() => new StanSubscriptionOptions();

        /// <summary>
        /// Returns new subscription options using available values from the provided options. 
        /// Default values will be used for values not available in the provided options.
        /// </summary>
        public static StanSubscriptionOptions GetFrom(StanSubscriptionOptions opts) => new StanSubscriptionOptions(opts);
    }
}
