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
using Google.Protobuf;

namespace STAN.Client
{
    // Keep protocol serialization encapulated here.
    internal class ProtocolSerializer
    {
        internal static byte[] Marshal(object req) => ((IMessage)req).ToByteArray();

        internal static void Unmarshal(byte[] bytes, object obj) => ((IMessage)obj).MergeFrom(bytes);

        internal static byte[] CreatePubMsg(string clientID, string guidValue, string subject, byte[] data, ByteString connId)
        {
            var pm = new PubMsg
            {
                ClientID = clientID,
                Guid = guidValue,
                Subject = subject,
                ConnID = connId,
                Data = data == null ? ByteString.Empty : ByteString.CopyFrom(data),
            };
            return pm.ToByteArray();
        }

        internal static byte[] CreateAck(MsgProto mp)
        {
            var ack = new Ack
            {
                Subject = mp.Subject,
                Sequence = mp.Sequence,
            };
            return ack.ToByteArray();
        }

        internal static byte[] CreatePing(ByteString connId) => new Ping { ConnID = connId }.ToByteArray();
    }
}
