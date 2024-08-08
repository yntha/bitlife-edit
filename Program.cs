using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using System.Text;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Collections;
using CommandLine;

public class Program
{
    class BitLifeEditOptions
    {
        [Value(0, MetaName = "input", Required = true, HelpText = "The input file to process.")]
        public required string InputFile { get; set; }

        [Option('o', "output", Required = false, HelpText = "The output file to write the JSON data to.")]
        public string? OutputFile { get; set; }

        [Option('m', "mono", Required = false, HelpText = "The path to the Mono DLL files.")]
        public string? MonoDLLPath { get; set; }

        [Option('d', "decrypt", Required = false, HelpText = "Decrypt the input file.")]
        public bool Decrypt { get; set; }

        [Option('e', "encrypt", Required = false, HelpText = "Encrypt the input file.")]
        public bool Encrypt { get; set; }

        [Option('c', "cipher", Required = false, HelpText = "Overrides the default cipher key used to decrypt var files.")]
        public string? CipherKey { get; set; }

        [Option('s', "save", Required = false, HelpText = "Dump the save game data to a JSON file.")]
        public bool Save { get; set; }

        [Option('l', "load", Required = false, HelpText = "Overwrite save game data based on variables stored in a JSON file.")]
        public bool Load { get; set; }

        [Option('f', "file", Required = false, HelpText = "The JSON file to load data from.")]
        public string? JSONFile { get; set; }

        [Option("max_depth", Required = false, HelpText = "The maximum depth to traverse when serializing the save game data. Default is 0 (no limit).", Default = 0)]
        public int MaxDepth { get; set; }
    };

    private const string DEFAULT_CIPHER_KEY = "com.wtfapps.apollo16";
    private static string assemblyPath = "";
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

    // there really isnt a header for binaryformatter serialized objects, so we'll just use the first 4 bytes of the file
    private static readonly byte[] saveGameHeader = {
        0x00, 0x01, 0x00, 0x00
    };
    private static BitLifeEditOptions options;

    private static object? Deserialize(byte[] inputData)
    {
        object? deserialized = null;

        try
        {
            using MemoryStream memoryStream = new(inputData);

#pragma warning disable SYSLIB0011
            BinaryFormatter binaryFormatter = new();
#pragma warning restore SYSLIB0011

            deserialized = binaryFormatter.Deserialize(memoryStream);
        }
        catch (Exception e)
        {
            Console.WriteLine("Deserializer Error: " + e.Message);
        }

        return deserialized;
    }

    private static string GetCipheredItem(string item, string obfuscatedCipherKey)
    {
        StringBuilder cipheredItem = new();
        int i = 0;

        while (i < item.Length)
        {
            cipheredItem.Append((char)(obfuscatedCipherKey[i % obfuscatedCipherKey.Length] ^ item[i]));
            i++;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(cipheredItem.ToString()));
    }

    // encrypt the json file in inputFile to a var file
    private static void EncryptVarFile()
    {
        string json = File.ReadAllText(options.InputFile);
        Dictionary<string, object>? itemMap = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        if (itemMap == null)
        {
            Console.WriteLine("Invalid JSON file.");
            return;
        }

        string obfuscatedCipherKey = "";
        string cK = options.CipherKey ?? DEFAULT_CIPHER_KEY;

        foreach (char c in cK.ToLower())
        {
            int obfChar;

            if (!obfCharMap.ContainsKey(c))
            {
                obfChar = c;
            }
            else
            {
                obfChar = obfCharMap[c];
            }

            obfuscatedCipherKey = string.Format("{0}{1}", obfuscatedCipherKey, (char)obfChar);
        }

        StringBuilder varFile = new();

        foreach (var item in itemMap)
        {
            string cipheredKey = GetCipheredItem(item.Key, obfuscatedCipherKey);

            // serialize the value to a byte array using binaryformatter
            using MemoryStream memoryStream = new();
#pragma warning disable SYSLIB0011 // Type or member is obsolete
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.Serialize(memoryStream, item.Value);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
            memoryStream.Position = 0;

            string serializedValue = Convert.ToBase64String(memoryStream.ToArray());
            string cipheredValue = GetCipheredItem(serializedValue, obfuscatedCipherKey);

            varFile.Append(string.Format("{0}:{1}\n", cipheredKey, cipheredValue));
        }

        string outputFile = options.OutputFile ?? options.InputFile + ".var";

        File.WriteAllText(outputFile, varFile.ToString());

        Console.WriteLine("Encrypted JSON file to: " + outputFile);
    }

    private static string GetDecipheredItem(string b64Item, string obfuscatedCipherKey)
    {
        StringBuilder decipheredItem = new();
        string b64DecodedItem = Encoding.UTF8.GetString(Convert.FromBase64String(b64Item));
        int i = 0;

        while (i < b64DecodedItem.Length)
        {
            decipheredItem.Append((char)(obfuscatedCipherKey[i % obfuscatedCipherKey.Length] ^ b64DecodedItem[i]));
            i++;
        }

        return decipheredItem.ToString();
    }

    private static void DecryptVarFile()
    {
        string[] fileLines = File.ReadAllLines(options.InputFile);
        Dictionary<string, object> itemMap = [];

        string obfuscatedCipherKey = "";
        string cK = options.CipherKey ?? DEFAULT_CIPHER_KEY;

        foreach (char c in cK)
        {
            int obfChar;

            if (!obfCharMap.ContainsKey(c))
            {
                obfChar = c;
            }
            else
            {
                obfChar = obfCharMap[c];
            }

            obfuscatedCipherKey = string.Format("{0}{1}", obfuscatedCipherKey, (char)obfChar);
        }

        foreach (string line in fileLines)
        {
            string[] lineItems = line.Split(':');
            string key = lineItems[0];
            string value = lineItems[1];

            string decipheredKey = GetDecipheredItem(key, obfuscatedCipherKey);
            string decipheredValue = GetDecipheredItem(value, obfuscatedCipherKey);

            // the deciphered value is a base64 encoded string that represents a serialized object.
            byte[] serializedData = Convert.FromBase64String(decipheredValue.ToString());
            object? deserialized = Deserialize(serializedData);

            if (deserialized != null)
            {
                itemMap.Add(decipheredKey.ToString(), deserialized);
            }
        }

        string json = JsonSerializer.Serialize(itemMap, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
        string outputFile = options.OutputFile ?? options.InputFile + ".json";

        File.WriteAllText(outputFile, json);

        Console.WriteLine("Decrypted var file to: " + outputFile);
    }

    private static Assembly MonoAssemblyResolver(object? sender, ResolveEventArgs args)
    {
        string? assemblyName = new AssemblyName(args.Name).Name;
        string assemblyFilePath = Path.Combine(options.MonoDLLPath!, assemblyName + ".dll");

        if (File.Exists(assemblyFilePath))
        {
            return Assembly.LoadFrom(assemblyFilePath);
        }
        else
        {
            throw new FileNotFoundException("Assembly not found: " + assemblyFilePath);
        }
    }

    private static Life? GetDeserializedSaveGame(string inputFile)
    {
        if (options.MonoDLLPath == null)
        {
            Console.WriteLine("The Mono DLL path is required to deserialize the save game data.");
            return null;
        }

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MonoAssemblyResolver);
        return (Life?)Deserialize(File.ReadAllBytes(inputFile));
    }

    private static void OverwriteSaveGameValues()
    {
        JsonSerializerOptions jsonSerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new SaveDataJSONConverter() },
            WriteIndented = true,
            IncludeFields = true
        };
        Life? deserialized = GetDeserializedSaveGame(options.InputFile);

        if (deserialized != null)
        {
            string json = File.ReadAllText(options.JSONFile!);
            Dictionary<string, object>? data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (data == null)
            {
                Console.WriteLine("Invalid JSON file.");
                return;
            }

            foreach (var item in data)
            {
                FieldInfo? field = deserialized.GetType().GetField(item.Key);

                if (field != null)
                {
                    field.SetValue(deserialized, item.Value);
                }
                else
                {
                    Console.WriteLine("Field not found: " + item.Key);
                }
            }

            // serialize to a stream
            using MemoryStream memoryStream = new();
#pragma warning disable SYSLIB0011 // Type or member is obsolete
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.Serialize(memoryStream, data);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
            File.WriteAllBytes(options.InputFile, memoryStream.ToArray());

            Console.WriteLine("Overwrote save game data with values from: " + options.JSONFile);
        }
        else
        {
            Console.WriteLine("Failed to deserialize the input file. Serializer returned null.");
        }
    }

    private static void DumpSaveGame()
    {
        JsonSerializerOptions jsonSerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new SaveDataJSONConverter() },
            WriteIndented = true,
            IncludeFields = true
        };
        Life? deserialized = GetDeserializedSaveGame(options.InputFile);

        if (deserialized != null)
        {
            string json = JsonSerializer.Serialize(deserialized, jsonSerializerOptions);
            string unescaped = Regex.Unescape(json);
            string outputFile = options.OutputFile ?? options.InputFile + ".json";

            File.WriteAllText(outputFile, unescaped);

            Console.WriteLine("Dumped save game data to: " + outputFile);
        }
        else
        {
            Console.WriteLine("Failed to deserialize the input file. Serializer returned null.");
        }
    }

    public static void Main(string[] args)
    {
        options = Parser.Default.ParseArguments<BitLifeEditOptions>(args).Value;

        byte[] fileHeader = File.ReadAllBytes(options.InputFile).Take(8).ToArray();

        // check if this is a save game file
        if (fileHeader.SequenceEqual(saveGameHeader))
        {
            Console.WriteLine("Auto-detected a save game file. Actions will be limited to dumping only.");

            DumpSaveGame();

            return;
        }

        if (options.Save) { DumpSaveGame(); return; }

        if (options.Load) { OverwriteSaveGameValues(); return; }

        if (options.Decrypt) { DecryptVarFile(); return; }

        if (options.Encrypt) { EncryptVarFile(); return; }
    }
}

public class SaveDataJSONConverter : JsonConverter<Life>
{
    public override Life? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Life value, JsonSerializerOptions options)
    {
        var props = this.Collect(value);

        var ser = JsonSerializer.Serialize(props, options);

        writer.WriteStringValue(ser);
    }

    // recursively add all fields to a dictionary and return it
    private Dictionary<string, object> Collect(object obj)
    {
        var props = obj.GetType()
                             .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .ToDictionary(x => x.Name, x => x.GetValue(obj));

        foreach (var prop in props)
        {
            if (prop.Value != null)
            {
                if (prop.Value is not IEnumerable && prop.Value.GetType().IsClass && prop.Value.GetType() != typeof(string))
                {
                    props[prop.Key] = Collect(prop.Value);
                }
                else
                {
                    // check if this is a collection
                    if (prop.Value is IEnumerable enumerable && !(prop.Value is string))
                    {
                        Console.WriteLine("Found enumerable: " + prop.Key);
                        Console.WriteLine("Type: " + prop.Value.GetType());

                        List<object> list = new();

                        foreach (var item in enumerable)
                        {
                            if (item is string)
                            {
                                list.Add(item);
                                continue;
                            }

                            list.Add(Collect(item));
                        }

                        props[prop.Key] = list;

                        continue;
                    }

                    props[prop.Key] = prop.Value;
                }
            }
        }

        return props;
    }
}
