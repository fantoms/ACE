﻿using ACE.Common.Cryptography;
using ACE.Managers;
using ACE.Network.Sequence;

namespace ACE.Network
{
    public class SessionConnectionData
    {
        public UIntSequence PacketSequence { get; set; }
        public uint FragmentSequence { get; set; }
        public ISAAC IssacClient { get; }
        public ISAAC IssacServer { get; }

        public double ServerTime { get; set; }

        public SessionConnectionData()
        {
            IssacClient = new ISAAC(ISAAC.ClientSeed);
            IssacServer = new ISAAC(ISAAC.ServerSeed);
            PacketSequence = new UIntSequence(false);
            ServerTime = WorldManager.PortalYearTicks;
        }
    }
}