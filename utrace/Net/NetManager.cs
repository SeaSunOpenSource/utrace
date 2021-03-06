/*!lic_info

The MIT License (MIT)

Copyright (c) 2015 SeaSunOpenSource

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
 
using Timer = System.Timers.Timer;

namespace utrace
{
    public class NetManager : IDisposable
    {
        public static NetManager Instance;

        public bool IsConnected { get { return _client.IsConnected; } }
        public string RemoteAddr { get { return _client.RemoteAddr; } }

        public event SysPost.StdMulticastDelegation LogicallyConnected;
        public event SysPost.StdMulticastDelegation LogicallyDisconnected;

        public NetManager()
        {
            _client.Connected += OnConnected;
            _client.Disconnected += OnDisconnected;
            _client.Prompted += OnPrompted;

            _client.RegisterCmdHandler(eNetCmd.SV_KeepAliveResponse, Handle_KeepAliveResponse);
            _client.RegisterCmdHandler(eNetCmd.SV_ExecCommandResponse, Handle_ExecCommandResponse);
            _client.RegisterCmdHandler(eNetCmd.SV_App_Logging, Handle_ServerLogging);

            _guardTimer.Timeout += OnGuardingTimeout;

            _tickTimer.Elapsed += (object sender, global::System.Timers.ElapsedEventArgs e) => Tick();
            _tickTimer.AutoReset = true;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public bool Connect(string addr)
        {
            int port = 0;
            if (!int.TryParse(Properties.Settings.Default.ServerPort, out port))
            {
                UsLogging.Printf(LogWndOpt.Error, "Properties.Settings.Default.ServerPort '{0}' invalid, connection aborted.", Properties.Settings.Default.ServerPort);
                return false;
            }

            _client.Connect(addr, port);
            return true;
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public void Send(UsCmd cmd)
        {
            _client.SendPacket(cmd);
        }

        public void RegisterCmdHandler(eNetCmd cmd, UsCmdHandler handler)
        {
            _client.RegisterCmdHandler(cmd, handler);
        }

        public void ExecuteCmd(string cmdText)
        {
            if (!IsConnected)
            {
                UsLogging.Printf(LogWndOpt.Bold, "not connected to server, command ignored.");
                return;
            }

            if (cmdText.Length == 0)
            {
                UsLogging.Printf(LogWndOpt.Bold, "the command bar is empty, try 'help' to list all supported commands.");
                return;
            }

            _client.SendText(cmdText + '\n');

            UsLogging.Printf(string.Format("command executed: [b]{0}[/b]", cmdText));
        }

        private void OnConnected(object sender, EventArgs e)
        {
            _tickTimer.Start();
            _guardTimer.Activate();
        }

        private void OnDisconnected(object sender, EventArgs e)
        {
            _tickTimer.Stop();
            _guardTimer.Deactivate();

            SysPost.InvokeMulticast(this, LogicallyDisconnected);
        }

        private void OnPrompted(object sender, EventArgs e)
        {
            if (_guardTimer.IsActive)
            {
                UsLogging.Printf("Server prompt received for the first time, connection validated.");
                _guardTimer.Deactivate();

                SysPost.InvokeMulticast(this, LogicallyConnected);
            }
        }

        private void OnGuardingTimeout(object sender, EventArgs e)
        {
            UsLogging.Printf(LogWndOpt.Error, "guarding timeout, closing connection...");
            Disconnect();
        }

        private bool Handle_KeepAliveResponse(eNetCmd cmd, UsCmd c)
        {
            //UsLogging.Printf("'KeepAlive' received.");
            return true;
        }

        private bool Handle_ServerLogging(eNetCmd cmd, UsCmd c)
        {
            UsLogPacket pkt = new UsLogPacket(c);
            UsNetLogging.Print(pkt);
            return true;
        }

        private bool Handle_ExecCommandResponse(eNetCmd cmd, UsCmd c)
        {
            int code = c.ReadInt32();
            UsLogging.Printf(string.Format("command executing result: [b]{0}[/b]", code));

            return true;
        }

        private long INTERVAL_KeepAlive = 3000;
        private long INTERVAL_CheckingConnectionStatus = 1000;
        private long INTERVAL_ReceivingData = 200;

        private long _currentTimeInMilliseconds = 0;
        private long _lastKeepAlive = 0;
        private long _lastCheckingConnectionStatus = 0;
        private long _lastReceivingData = 0;
        private void Tick()
        {
            if (!_client.IsConnected)
                return;

            _currentTimeInMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            if (_currentTimeInMilliseconds - _lastKeepAlive > INTERVAL_KeepAlive)
            {
                _lastKeepAlive = _currentTimeInMilliseconds;
            }

            if (_currentTimeInMilliseconds - _lastCheckingConnectionStatus > INTERVAL_CheckingConnectionStatus)
            {
                _client.Tick_CheckConnectionStatus();
                _lastCheckingConnectionStatus = _currentTimeInMilliseconds;
            }

            if (_currentTimeInMilliseconds - _lastReceivingData > INTERVAL_ReceivingData)
            {
                _client.Tick_ReceivingData();
                _lastReceivingData = _currentTimeInMilliseconds;
            }
        }

        private NetClient _client = new NetClient();
        private NetGuardTimer _guardTimer = new NetGuardTimer();
        private Timer _tickTimer = new Timer(100);
    }
}
