using ACE.Entity.Enum;
using ACE.Network;
using ACE.Network.GameEvent.Events;
using System;

namespace ACE.Managers
{
    public static class ChatManager
    {
        private static readonly object chatMutex = new object();

        /// <summary>
        /// Determines if the current session has access to a specific group chat channel.
        /// </summary>
        private static bool HasPermission(Session session, ChannelChatType chatType)
        {
            // Check the Character.AvailablChannels to see if the channel is available
            // If the channel is available, do a final user permission check, to make sure the player has the same group access
            return true;
        }

        public static void PerformGroupChat(Session session, ChannelChatType groupChatType, string message)
        {
            lock (chatMutex)
            {
                switch (groupChatType)
                {
                    case ChannelChatType.TellAbuse:
                        {
                            // TODO: Proper permissions check. This command should work for any character with AccessLevel.Advocate or higher
                            // if (!session.Player.IsAdmin)
                            //    break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                            {
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    if (recipient.AccessLevel >= AccessLevel.Advocate)
                                        recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                            }
                        }
                        break;
                    case ChannelChatType.TellAdmin:
                        {
                            if (!session.Player.IsAdmin)
                                break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                            // NetworkManager.SendWorldMessage(recipient, gameMessageSystemChat);
                        }
                        break;
                    case ChannelChatType.TellAudit:
                        {
                            // TODO: Proper permissions check. This command should work for any character AccessLevel.Sentinel or higher
                            // if (!session.Player.IsAdmin)
                            //    break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    if (recipient.AccessLevel >= AccessLevel.Sentinel)
                                        recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                        }
                        break;
                    case ChannelChatType.TellAdvocate:
                        {
                            // TODO: Proper permissions check. This command should work for any character AccessLevel.Advocate or higher
                            // if (!session.Player.IsAdmin)
                            //    break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                        }
                        break;
                    case ChannelChatType.TellAdvocate2:
                        {
                            // TODO: Proper permissions check. This command should work for any character AccessLevel.Advocate or higher
                            // if (!session.Player.IsAdmin)
                            //    break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                        }
                        break;
                    case ChannelChatType.TellAdvocate3:
                        {
                            // TODO: Proper permissions check. This command should work for any character AccessLevel.Advocate or higher
                            // if (!session.Player.IsAdmin)
                            //    break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                        }
                        break;
                    case ChannelChatType.TellSentinel:
                        {
                            // TODO: Proper permissions check. This command should work for any character with AccessLevel.Sentinel or higher
                            // if (!session.Player.IsAdmin)
                            //    break;

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                                else
                                    recipient.Network.EnqueueSend(new GameEventChannelBroadcast(recipient, groupChatType, "", message));
                        }
                        break;
                    case ChannelChatType.TellHelp:
                        {
                            ChatPacket.SendServerMessage(session, "GameActionChatChannel TellHelp Needs work.", ChatMessageType.Broadcast);
                            // TODO: I don't remember exactly how this was triggered. I don't think it sent back a "You say" message to the person who triggered it
                            // TODO: Proper permissions check. Requesting urgent help should work for any character but only displays the "says" mesage for those subscribed to the Help channel
                            //      which would be Advocates and above.
                            // if (!session.Player.IsAdmin)
                            //    break;
                            string onTheWhatChannel = "on the " + System.Enum.GetName(typeof(ChannelChatType), groupChatType).Replace("Tell", "") + " channel";
                            string whoSays = session.Player.Name + " says ";

                            // ChatPacket.SendServerMessage(session, $"You say {onTheWhatChannel}, \"{message}\"", ChatMessageType.OutgoingHelpSay);

                            var gameMessageSystemChat = new Network.GameMessages.Messages.GameMessageSystemChat(whoSays + onTheWhatChannel + ", \"" + message + "\"", ChatMessageType.Help);

                            // TODO This should check if the recipient is subscribed to the channel
                            foreach (var recipient in WorldManager.GetAll())
                                if (recipient != session)
                                    recipient.Network.EnqueueSend(gameMessageSystemChat);

                            // again not sure what way to go with this.. the code below was added after I realized I should be handling things differently
                            // and by handling differently I mean letting the client do all of the work it was already designed to do.

                            // foreach (var recipient in WorldManager.GetAll())
                            //    if (recipient != session)
                            //        NetworkManager.SendWorldMessage(recipient, new GameEvent.Events.GameEventChannelBroadcast(recipient, groupChatType, session.Player.Name, message));
                            //    else
                            //        NetworkManager.SendWorldMessage(recipient, new GameEvent.Events.GameEventChannelBroadcast(recipient, groupChatType, "", message));
                        }
                        break;

                    case ChannelChatType.TellFellowship:
                        {
                            var statusMessage = new GameEventDisplayStatusMessage(session, StatusMessageType1.YouDoNotBelongToAFellowship);
                            session.Network.EnqueueSend(statusMessage);

                            ChatPacket.SendServerMessage(session, "GameActionChatChannel TellFellowship Needs work.", ChatMessageType.Broadcast);
                        }
                        break;

                    case ChannelChatType.TellVassals:
                        {
                            ChatPacket.SendServerMessage(session, "GameActionChatChannel TellVassals Needs work.", ChatMessageType.Broadcast);
                        }
                        break;

                    case ChannelChatType.TellPatron:
                        {
                            ChatPacket.SendServerMessage(session, "GameActionChatChannel TellPatron Needs work.", ChatMessageType.Broadcast);
                        }
                        break;

                    case ChannelChatType.TellMonarch:
                        {
                            ChatPacket.SendServerMessage(session, "GameActionChatChannel TellMonarch Needs work.", ChatMessageType.Broadcast);
                        }
                        break;

                    case ChannelChatType.TellCoVassals:
                        {
                            ChatPacket.SendServerMessage(session, "GameActionChatChannel TellCoVassals Needs work.", ChatMessageType.Broadcast);
                        }
                        break;

                    case ChannelChatType.AllegianceBroadcast:
                        {
                            // The client knows if we're in an allegiance or not, and will throw an error to the user if they try to /a, and no message will be dispatched to the server.

                            ChatPacket.SendServerMessage(session, "GameActionChatChannel AllegianceBroadcast Needs work.", ChatMessageType.Broadcast);
                        }
                        break;

                    default:
                        Console.WriteLine($"Unhandled ChatChannel GroupChatType: 0x{(uint)groupChatType:X4}");
                        break;
                }
            }
        }
    }
}
