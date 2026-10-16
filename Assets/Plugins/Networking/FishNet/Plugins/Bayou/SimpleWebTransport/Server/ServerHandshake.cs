using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JamesFrowen.SimpleWeb
{
    /// <summary>
    /// Handles Handshakes from new clients on the server
    /// <para>The server handshake has buffers to reduce allocations when clients connect</para>
    /// </summary>
    internal class ServerHandshake
    {
        const int GetSize = 3;
        const int ResponseLength = 129;
        const int KeyLength = 24;
        const int MergedKeyLength = 60;
        // this isnt an offical max, just a reasonable size for a websocket handshake
        readonly int maxHttpHeaderSize = 3000;

        readonly SHA1 sha1 = SHA1.Create();
        readonly BufferPool bufferPool;

        public ServerHandshake(BufferPool bufferPool, int handshakeMaxSize)
        {
            this.bufferPool = bufferPool;
            this.maxHttpHeaderSize = handshakeMaxSize;
        }

        ~ServerHandshake()
        {
            sha1.Dispose();
        }

        public bool TryHandshake(Connection conn)
        {
            Stream stream = conn.stream;

            using (ArrayBuffer getHeader = bufferPool.Take(GetSize))
            {
                if (!ReadHelper.TryRead(stream, getHeader.array, 0, GetSize))
                    return false;
                getHeader.count = GetSize;


                if (!IsGet(getHeader.array))
                {
                    Log.Warn($"First bytes from client was not 'GET' for handshake, instead was {Log.BufferToString(getHeader.array, 0, GetSize)}");
                    return false;
                }
            }


            string msg = ReadToEndForHandshake(stream);

            if (string.IsNullOrEmpty(msg))
                return false;

            try
            {
                AcceptHandshake(stream, msg);
                return true;
            }
            catch (ArgumentException e)
            {
                Log.InfoException(e);
                return false;
            }
        }

        string ReadToEndForHandshake(Stream stream)
        {
            using (ArrayBuffer readBuffer = bufferPool.Take(maxHttpHeaderSize))
            {
                int? readCountOrFail = ReadHelper.SafeReadTillMatch(stream, readBuffer.array, 0, maxHttpHeaderSize, Constants.endOfHandshake);
                if (!readCountOrFail.HasValue)
                    return null;

                int readCount = readCountOrFail.Value;

                string msg = Encoding.ASCII.GetString(readBuffer.array, 0, readCount);
                Log.Verbose(msg);

                return msg;
            }
        }

        static bool IsGet(byte[] getHeader)
        {
            // just check bytes here instead of using Encoding.ASCII
            return getHeader[0] == 71 && // G
                   getHeader[1] == 69 && // E
                   getHeader[2] == 84;   // T
        }

        void AcceptHandshake(Stream stream, string msg)
        {
            using (
                ArrayBuffer keyBuffer = bufferPool.Take(KeyLength + Constants.HandshakeGUIDLength),
                            responseBuffer = bufferPool.Take(ResponseLength))
            {
                GetKey(msg, keyBuffer.array);
                AppendGuid(keyBuffer.array);
                byte[] keyHash = CreateHash(keyBuffer.array);
                CreateResponse(keyHash, responseBuffer.array);

                stream.Write(responseBuffer.array, 0, ResponseLength);
            }
        }


        static void GetKey(string msg, byte[] keyBuffer)
        {
            // HTTP field names are case-insensitive. Proxies and non-browser
            // clients can legally send sec-websocket-key in lowercase.
            string key = null;
            foreach (var line in msg.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                {
                    key = line.Substring(colon + 1).Trim();
                    break;
                }
            }
            if (key == null || key.Length != KeyLength)
                throw new ArgumentException("A valid Sec-WebSocket-Key header is required.");
            try
            {
                if (Convert.FromBase64String(key).Length != 16) throw new ArgumentException("WebSocket key must encode 16 bytes.");
            }
            catch (FormatException) { throw new ArgumentException("WebSocket key must be Base64."); }

            Encoding.ASCII.GetBytes(key, 0, KeyLength, keyBuffer, 0);
        }

        static void AppendGuid(byte[] keyBuffer)
        {
            Buffer.BlockCopy(Constants.HandshakeGUIDBytes, 0, keyBuffer, KeyLength, Constants.HandshakeGUIDLength);
        }

        byte[] CreateHash(byte[] keyBuffer)
        {
            Log.Verbose($"Handshake Hashing {Encoding.ASCII.GetString(keyBuffer, 0, MergedKeyLength)}");

            return sha1.ComputeHash(keyBuffer, 0, MergedKeyLength);
        }

        static void CreateResponse(byte[] keyHash, byte[] responseBuffer)
        {
            string keyHashString = Convert.ToBase64String(keyHash);

            // compiler should merge these strings into 1 string before format
            string message = string.Format(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: {0}\r\n\r\n",
                keyHashString);

            Log.Verbose($"Handshake Response length {message.Length}, IsExpected {message.Length == ResponseLength}");
            Encoding.ASCII.GetBytes(message, 0, ResponseLength, responseBuffer, 0);
        }
    }
}
