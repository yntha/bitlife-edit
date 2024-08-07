using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using System.Text;

public class Program {
    private const string DEFAULT_CIPHER_KEY = "com.wtfapps.apollo16";
    private static readonly Dictionary<int, int> obfCharMap = new() {
        {0x62, 0x6d}, {0x63, 0x79}, {0x64, 0x6c},
        {0x65, 0x78}, {0x66, 0x6b}, {0x67, 0x77},
        {0x68, 0x6a}, {0x69, 0x76}, {0x6a, 0x69},
        {0x6b, 0x75}, {0x6c, 0x68}, {0x6d, 0x74},
        {0x6e, 0x67}, {0x6f, 0x73}, {0x70, 0x66},
        {0x71, 0x72}, {0x72, 0x65}, {0x73, 0x71},
        {0x74, 0x64}, {0x75, 0x70}, {0x76, 0x63},
        {0x77, 0x6f}, {0x78, 0x62}, {0x79, 0x6e},
        {0x7a, 0x61}, {0x61, 0x7a}
    };

    private static object? Deserialize(byte[] inputData) {
        object? deserialized = null;

        try {
            using MemoryStream memoryStream = new(inputData);

#pragma warning disable SYSLIB0011 // Type or member is obsolete
            BinaryFormatter binaryFormatter = new();
#pragma warning restore SYSLIB0011 // Type or member is obsolete

            deserialized = binaryFormatter.Deserialize(memoryStream);
        } catch (Exception e) {
            Console.WriteLine("Deserializer Error: " + e.Message);
        }

        return deserialized;
    }

    private static string GetDecipheredItem(string b64Item, string obfuscatedCipherKey) {
        StringBuilder decipheredItem = new();
        string b64DecodedItem = Encoding.UTF8.GetString(Convert.FromBase64String(b64Item));
        int i = 0;
        int j = 0;

        while (i < b64DecodedItem.Length) {
            if (j >= obfuscatedCipherKey.Length) {
                j = 0;
            }

            decipheredItem.Append((char)(obfuscatedCipherKey[j] ^ b64DecodedItem[i]));
            i++;
            j++;
        }

        return decipheredItem.ToString();
    }

    private static string Decrypt(string inputFile) {
        string[] fileLines = File.ReadAllLines(inputFile);
        Dictionary<string, object> itemMap = [];

        Console.WriteLine("Items: " + fileLines.Length);

        string obfuscatedCipherKey = "";
        string cK = DEFAULT_CIPHER_KEY.ToLower();

        foreach (char c in cK) {
            int obfChar;

            if (!obfCharMap.ContainsKey(c)) {
                obfChar = c;
            } else {
                obfChar = obfCharMap[c];
            }

            obfuscatedCipherKey = string.Format("{0}{1}", obfuscatedCipherKey, (char)obfChar);
        }

        foreach (string line in fileLines) {
            Console.WriteLine("Decoding item: " + line);

            string[] lineItems = line.Split(':');
            string key = lineItems[0];
            string value = lineItems[1];

            string decipheredKey = GetDecipheredItem(key, obfuscatedCipherKey);
            string decipheredValue = GetDecipheredItem(value, obfuscatedCipherKey);

            Console.WriteLine("Deciphered key: " + decipheredKey);
            Console.WriteLine("Deciphered value: " + decipheredValue.ToString() + "\n");

            // the deciphered value is a base64 encoded string that represents a serialized object.
            byte[] serializedData = Convert.FromBase64String(decipheredValue.ToString());
            object? deserialized = Deserialize(serializedData);

            if (deserialized != null) {
                itemMap.Add(decipheredKey.ToString(), deserialized);
            }
        }

        return JsonSerializer.Serialize(itemMap, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void Main(string[] args) {
        string inputFile = args[0];

        if (inputFile.EndsWith(".data")) {
            // this is a serialized file with no encryption.
            object? deserialized = Deserialize(File.ReadAllBytes(inputFile));

            if (deserialized != null) {
                File.WriteAllText(inputFile + ".json", JsonSerializer.Serialize(deserialized, new JsonSerializerOptions { WriteIndented = true }));
            } else {
                Console.WriteLine("Failed to deserialize the input file. Serializer returned null.");
            }

            return;
        }

        // this is most likely one of the encrypted files. each line in the file is
        // an encrypted key-value pair. the value, however, represents a serialized
        // object that must be deserialized with the BinaryFormatter class.
        File.WriteAllText(inputFile + ".json", Decrypt(inputFile));
    }
}
