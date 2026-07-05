using AuthService.Contracts;
using SharedContracts.ConnectController;
using SharedContracts.Messages;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class DemoTCP : MonoBehaviour
{
#if UNITY_EDITOR

#endif

    [SerializeField] string message = "Hello, Server!";
    [SerializeField] long idDemo = 1234;
    [SerializeField] string host = "https:localhost";
    [SerializeField] int port = 0000;
    [SerializeField] bool checkConnection = false;
    TcpClient client;
    [HideInInspector]
    StreamConnectControllerMessage streamConnectController;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    [Button("Connect")]
    public async Task<TcpClient?> ConnectTcpAsync()
    {
        try
        {
            //SharedContracts.LogUltil.Debug
            client = new TcpClient();
            await client.ConnectAsync(host, port);
            if (client.Connected)
            {
                await SendIDAsync();
                streamConnectController = new StreamConnectControllerMessage(client);
                Debug.Log($"✅ Kết nối TCP thành công đến {host}:{port}");
            }
            return client;
        }
        catch (Exception ex)
        {
            Debug.Log($"❌ Lỗi khi kết nối TCP: {ex.Message}");
            return null;
        }
    }

    [Button("Send Message")]
    public void SendAsync()
    {
        if (client?.Connected != true) return;

        //SystemInformation.Device.DeviceUUID
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        CMLogin loginMessage = new CMLogin() { deviceId = deviceId };
        byte[] data = loginMessage.SerializeMessage();

        streamConnectController.SendMessage(loginMessage, true);
    }
    public async Task SendIDAsync()
    {
        if (client?.Connected != true) return;
        var stream = client.GetStream();
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        
        ushort lengthId = (ushort)deviceId.Length;
        Debug.Log("Gửi DeviceID: " + deviceId+" Length: "+lengthId);
        byte[] data = BitConverter.GetBytes(lengthId);
        await stream.WriteAsync(data, 0, data.Length);
        byte[] idBytes = Encoding.UTF8.GetBytes(deviceId);
        await stream.WriteAsync(idBytes, 0, idBytes.Length);


    }

    [Button("Check TypeID")]
    public void Check()
    {
        SMLogin loginMessage = new SMLogin();
        Debug.Log("Gửi message SMLogin: " + loginMessage.MessageTypeId);
    }
    [Button("Disconnect")]
    public void Disconnect()
    {
        if (client != null && client.Connected)
        {
            client.Close();
            Debug.Log("Đã ngắt kết nối TCP.");
        }
    }
    private void OnDestroy()
    {
        Disconnect();
    }

}

