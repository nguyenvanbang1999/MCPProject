using System;
using System.Collections.Generic;
using System.Text;

namespace SharedContracts.Messages
{
    public abstract class MessageReviceController
    {
        internal abstract void OnReveive(MessageBase messageBase);
        
    }
    public abstract class MessageReviceController<Message>: MessageReviceController where Message : MessageBase
    {

        protected abstract void OnReveive(Message message);

        internal override void OnReveive(MessageBase messageBase)
        {
            OnReveive((Message)messageBase);
        }
    }
}
