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
using System;
using System.Threading;
using System.Diagnostics;
#if NET45
using System.Reflection;
#endif
using NATS.Client;

using System.IO;

namespace STAN.Client.UnitTests
{
    class RunnableServer : IDisposable
    {
        Process p;
        string executablePath;

        public void init(string exeName, string args)
        {
            UnitTestUtilities.CleanupExistingServers(exeName);
            executablePath = exeName + ".exe";
            ProcessStartInfo psInfo = createProcessStartInfo(args);
            try
            {
                p = Process.Start(psInfo);
                for (int i = 1; i <= 20; i++)
                {
                    Thread.Sleep(100 * i);
                    if (IsRunning())
                        break;
                }

                if (p.HasExited)
                {
                    throw new Exception("Unable to start process.");
                }

                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                p = null;
                throw new Exception(string.Format("{0} {1} failure with error: {2}", psInfo.FileName, psInfo.Arguments, ex.Message));
            }
        }

        public RunnableServer(string exeName)
        {
            init(exeName, null);
        }

        public RunnableServer(string exeName, string args)
        {
            init(exeName, args);
        }

        private ProcessStartInfo createProcessStartInfo(string args)
        {
            ProcessStartInfo ps = new ProcessStartInfo(executablePath);
            ps.Arguments = args;
            ps.WorkingDirectory = UnitTestUtilities.GetConfigDir();
#if NET45
            ps.WindowStyle = ProcessWindowStyle.Hidden;
#else
            ps.CreateNoWindow = false;
            ps.RedirectStandardError = true;
#endif
            return ps;
        }

        public bool IsRunning()
        {
            try
            {
                new ConnectionFactory().CreateConnection().Close();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Shutdown()
        {
            if (p == null)
                return;

            try
            {
                p.Kill();
                p.WaitForExit(60000);
            }
            catch (Exception) { }

            p = null;
        }

        void IDisposable.Dispose()
        {
            Shutdown();
        }
    }

    class NatsServer : RunnableServer
    {
        public NatsServer() : base("gnatsd") { }
        public NatsServer(string args) : base("gnatsd", args) { }
    }

    class NatsStreamingServer : RunnableServer
    {
        public NatsStreamingServer() : base("nats-streaming-server") { }
        public NatsStreamingServer(string args) : base("nats-streaming-server", args) { }
    }

    class UnitTestUtilities
    {
        object mu = new object();

        static internal string GetConfigDir()
        {
#if NET45
            string baseDir = Assembly.GetExecutingAssembly().CodeBase;
            return baseDir + "\\NATSUnitTests\\config";
#else
            return AppContext.BaseDirectory +
                string.Format("{0}..{0}..{0}..{0}",
                Path.DirectorySeparatorChar);
#endif
        }

        internal static void CleanupExistingServers(string procname)
        {
            bool hadProc = false;
            try
            {
                Process[] procs = Process.GetProcessesByName(procname);

                foreach (Process proc in procs)
                {
                    proc.Kill();
                }
            }
            catch (Exception) { } // ignore

            if (hadProc) Thread.Sleep(500);
        }
    }
}
