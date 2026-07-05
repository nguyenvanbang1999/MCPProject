using AuthService.Contracts;
using MessagePack;
using SharedContracts.Messages;
using System;
using UnityEngine;


public class SMLoginHandle : MessageReviceController<SMLogin>
{
    public static event Action<SMLogin> OnLoginSuccess;

    public SMLoginHandle(SMLogin message) : base(message)
    {
    }

    protected override void OnReveive(SMLogin message)
    {
        Debug.Log("Nhận đc SMLogin: " + message.userId);
        OnLoginSuccess?.Invoke(message);
    }
}
