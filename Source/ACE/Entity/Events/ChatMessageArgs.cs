using ACE.Entity.Enum;
using System;

namespace ACE.Entity.Events
{
    public class ChatMessageArgs : EventArgs
    {
        public string Message { get; set; }

        public ChatMessageType MessageType { get; set; }

        public ChannelChatType Channel { get; private set; }

        public ChatMessageArgs(string message, ChatMessageType type)
        {
            this.Message = message;
            this.MessageType = type;
            this.Channel = ChannelChatType.Undef;
        }

        public ChatMessageArgs(string message, ChatMessageType type, ChannelChatType channel)
        {
            this.Message = message;
            this.MessageType = type;
            this.Channel = channel;
        }
    }
}
