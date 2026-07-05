using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.LogUltil;
using System.Reflection;


namespace SharedContracts.Messages
{
    /// <summary>
    /// Utility class for message serialization, deserialization, and routing.
    /// Handles automatic discovery of message types and their handlers.
    /// </summary>
    public static class MessageUtil
    {
        /// <summary>
        /// DI container tùy chọn do host application (Program.cs của từng service) gán sau khi build.
        /// Khi có giá trị, handler được tạo qua <see cref="ActivatorUtilities"/> nên constructor có thể
        /// nhận service từ DI (vd IEventBus). Khi null (vd Unity client không set), fallback về
        /// Activator.CreateInstance parameterless như trước — không phá vỡ tương thích ngược.
        /// Không đánh dấu '?' vì file này không bật '#nullable' (netstandard2.1, giữ nguyên convention gốc).
        /// </summary>
        public static IServiceProvider ServiceProvider { get; set; }

        private static List<Type> _allMessage;
        
        /// <summary>
        /// Gets all message types discovered in loaded assemblies.
        /// Cached after first access for performance.
        /// </summary>
        public static List<Type> AllMessage
        {
            get
            {
                if (_allMessage == null)
                {
                    _allMessage = GetAllTypeMessage();
                }
                return _allMessage;
            }
        }

        private static Dictionary<Type, Type> _mapTypeHandle;
        
        /// <summary>
        /// Gets the mapping of message types to their handler controllers.
        /// Key: Message type (e.g., CMLogin)
        /// Value: Handler type (e.g., CMLoginReviceCtrl)
        /// Cached after first access for performance.
        /// </summary>
        public static Dictionary<Type, Type> MapTypeMessageHandle
        {
            get
            {
                if (_mapTypeHandle == null)
                {
                    _mapTypeHandle = GetDicReviceHandle();
                }
                return _mapTypeHandle;
            }
        }
        
        /// <summary>
        /// Computes a unique hash ID for a message type using its class name.
        /// Uses MurmurHash3 algorithm for fast, consistent hashing.
        /// </summary>
        /// <param name="typeMessage">The message type to hash</param>
        /// <returns>A 32-bit hash value uniquely identifying this message type</returns>
        public static uint GetMessageTypeId(Type typeMessage)
        {
            string typeName = typeMessage.Name;

            return ComputeHash(typeName);
        }
        
        /// <summary>
        /// Computes a MurmurHash3 (32-bit) hash of a string.
        /// Fast, non-cryptographic hash function with good distribution.
        /// </summary>
        /// <param name="key">The string to hash</param>
        /// <param name="seed">Optional seed value for hash variation</param>
        /// <returns>A 32-bit hash value</returns>
        static uint ComputeHash(string key, uint seed = 0)
        {
            const uint c1 = 0xcc9e2d51;
            const uint c2 = 0x1b873593;
            uint h1 = seed;
            int length = key.Length;
            int i = 0;

            while (i + 1 < length)
            {
                uint k1 = (uint)(key[i] | key[i + 1] << 16);
                k1 *= c1;
                k1 = k1 << 15 | k1 >> 17;
                k1 *= c2;

                h1 ^= k1;
                h1 = h1 << 13 | h1 >> 19;
                h1 = h1 * 5 + 0xe6546b64;
                i += 2;
            }

            if (i < length)
            {
                uint k1 = key[i];
                k1 *= c1;
                k1 = k1 << 15 | k1 >> 17;
                k1 *= c2;
                h1 ^= k1;
            }

            h1 ^= (uint)(length * 2);
            h1 ^= h1 >> 16;
            h1 *= 0x85ebca6b;
            h1 ^= h1 >> 13;
            h1 *= 0xc2b2ae35;
            h1 ^= h1 >> 16;

            return h1;
        }

        /// <summary>
        /// Serializes a message to byte array for network transmission.
        /// Format: [4 bytes: message type ID][variable: MessagePack serialized content]
        /// </summary>
        /// <param name="message">The message to serialize</param>
        /// <returns>Byte array containing the serialized message</returns>
        public static byte[] SerializeMessage(this MessageBase message)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // Ghi loại tin nhắn
                    uint messageTypeId = GetMessageTypeId(message.GetType());
                    writer.Write(messageTypeId);
                    Debug.Log(messageTypeId + " type: " + message.GetType());

                    // Ghi nội dung tin nhắn
                    byte[] bytes = MessagePackSerializer.Serialize(message.GetType(), message);
                    Debug.Log(bytes);
                    writer.Write(bytes);
                }
                return ms.ToArray();
            }
        }
        
        /// <summary>
        /// Deserializes a byte array back into a message object.
        /// Automatically detects the message type from the embedded type ID.
        /// </summary>
        /// <param name="data">Byte array containing the serialized message</param>
        /// <param name="needWarning">Whether to log warnings for unknown message types</param>
        /// <returns>The deserialized message, or a base MessageBase if type unknown</returns>
        public static MessageBase DeserializeMessage(byte[] data, bool needWarning = true)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(ms))
                {
                    // Đọc loại tin nhắn
                    uint messageTypeId = reader.ReadUInt32();
                    // Tìm kiểu tin nhắn tương ứng
                    Type messageType = AllMessage.FirstOrDefault(t => GetMessageTypeId(t) == messageTypeId);
                    if (messageType == null)
                    {
                        if (needWarning)
                            Debug.LogWarning($"Không tìm thấy kiểu tin nhắn cho MessageTypeId: {messageTypeId}");
                        messageType = typeof(MessageBase);
                    }
                    // Đọc nội dung tin nhắn
                    byte[] messageBytes = reader.ReadBytes((int)(ms.Length - ms.Position));
                    var message = (MessageBase)MessagePackSerializer.Deserialize(messageType, messageBytes);
                    return message;
                }
            }
        }

        public static List<Type> GetAllTypeMessage()
        {
            var messageTypes = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract && typeof(MessageBase).IsAssignableFrom(t));
                    messageTypes.AddRange(types);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    var types = ex.Types
                        .Where(t => t != null && t.IsClass && !t.IsAbstract && typeof(MessageBase).IsAssignableFrom(t));
                    messageTypes.AddRange(types);
                }
            }
            foreach (var type in messageTypes)
            {
                Debug.Log($"Found message type: {type.FullName}, Hash: {GetMessageTypeId(type)}");
            }
            return messageTypes;
        }

        // Tạo dictionary ánh xạ từ kiểu MessageBase sang controller tương ứng kế thừa MessageReviceController<T>
        public static Dictionary<Type, Type> GetDicReviceHandle()
        {
            var result = new Dictionary<Type, Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                Type[] typesInAsm;
                try
                {
                    typesInAsm = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    typesInAsm = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in typesInAsm)
                {
                    if (type == null || !type.IsClass || type.IsAbstract)
                        continue;

                    // Duyệt lên chuỗi kế thừa để tìm base là MessageReviceController<> 
                    var current = type;
                    while (current != null && current != typeof(object))
                    {
                        if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(MessageReviceController<>))
                        {
                            var messageType = current.GetGenericArguments()[0];
                            if (messageType != null && typeof(MessageBase).IsAssignableFrom(messageType))
                            {
                                if (!result.ContainsKey(messageType))
                                {
                                    result.Add(messageType, type);
                                    Debug.Log($"Map message: {messageType.FullName} -> controller: {type.FullName}");
                                }
                                else
                                {
                                    Debug.Log($"Duplicate controller for {messageType.FullName}: {type.FullName}, existing: {result[messageType].FullName}");
                                }
                            }
                            break; // đã tìm thấy base phù hợp
                        }
                        current = current.BaseType;
                    }
                }
            }

            return result;
        }

        public static void OnReviceMessage(MessageBase message, bool needWarning = true)
        {
            if (message == null)
            {
                return;
            }
            if (MapTypeMessageHandle.TryGetValue(message.GetType(), out Type typeHandle))
            {
                MessageReviceController messageHandle = ServiceProvider != null
                    ? (MessageReviceController)ActivatorUtilities.CreateInstance(ServiceProvider, typeHandle)
                    : (MessageReviceController)Activator.CreateInstance(typeHandle);
                messageHandle.OnReveive(message);
            }
            else if (needWarning)
            {
                Debug.LogWarning($"Không tìm thấy handler cho message type: {message.GetType().FullName}");
            }
        }
    }
}
