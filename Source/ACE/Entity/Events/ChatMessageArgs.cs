using ACE.Entity.Enum;
using System;

namespace ACE.Entity.Events
{
    public class ChatMessageArgs : EventArgs
    {
        public string Message { get; set; }

        public ChatMessageType MessageType { get; set; }

        public GroupChatType Channel { get; private set; }

        public ChatMessageArgs(string message, ChatMessageType type)
        {
            this.Message = message;
            this.MessageType = type;
            this.Channel = GroupChatType.Undef;
        }

        public ChatMessageArgs(string message, ChatMessageType type, GroupChatType channel)
        {
            this.Message = message;
            this.MessageType = type;
            this.Channel = channel;
        }
    }
}
