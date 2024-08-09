using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using System.Text;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Collections;
using CommandLine;
using System.ComponentModel;

public class Program
{
    public class BitLifeEditOptions
    {
        [Value(0, MetaName = "input", Required = true, HelpText = "The input file to process.")]
        public required string InputFile { get; set; }

        [Option('o', "output", Required = false, HelpText = "The output file to write the JSON data to.")]
        public string? OutputFile { get; set; }

        [Option('m', "mono", Required = false, HelpText = "The path to the Mono DLL files.")]
        public string? MonoDLLPath { get; set; }

        [Option('d', "decrypt", Required = false, HelpText = "Decrypt the input var file.")]
        public bool Decrypt { get; set; }

        [Option('e', "encrypt", Required = false, HelpText = "Encrypt the input var file.")]
        public bool Encrypt { get; set; }

        [Option('c', "cipher", Required = false, HelpText = "Overrides the default cipher key used to decrypt var files.")]
        public string? CipherKey { get; set; }

        [Option('s', "save", Required = false, HelpText = "Dump a .data file to a JSON file.")]
        public bool Save { get; set; }

        [Option('p', "patch", Required = false, HelpText = "Patch any .data file based on variables stored in a JSON file.")]
        public bool Patch { get; set; }

        [Option('f', "file", Required = false, HelpText = "The JSON file containing the variables required for a patch.")]
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

        foreach (var key in itemMap.Keys)
        {
            string cipheredKey = GetCipheredItem(key, obfuscatedCipherKey);

            // serialize the value to a byte array using binaryformatter
            using MemoryStream memoryStream = new();

            // the formatter cant serialize jsonelement objects, so extract the value and serialize that
            if (itemMap[key] is JsonElement jsonElement)
            {
#pragma warning disable CS8601 // Possible null reference assignment.
                itemMap[key] = jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString(),
                    JsonValueKind.Number => jsonElement.TryGetInt64(out long l) ? l : jsonElement.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => jsonElement.EnumerateObject().ToDictionary(kv => kv.Name, kv => kv.Value),
                    JsonValueKind.Array => jsonElement.EnumerateArray().ToList()
                };
#pragma warning restore CS8601 // Possible null reference assignment.
            }
#pragma warning disable SYSLIB0011 // Type or member is obsolete
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.Serialize(memoryStream, itemMap[key]);
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

    private static void OverwriteDataFileValues(object deserializedData)
    {
        JsonSerializerOptions jsonSerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new DataFileJSONConverter<object>(options) },
            WriteIndented = true,
            IncludeFields = true
        };

        if (deserializedData != null)
        {
            string json = File.ReadAllText(options.JSONFile!);
            Dictionary<string, object>? data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (data == null)
            {
                Console.WriteLine("Invalid JSON file.");
                return;
            }

            foreach (var key in data.Keys)
            {
                if (data[key] is JsonElement jsonElement)
                {
#pragma warning disable CS8601 // Possible null reference assignment.
                    data[key] = jsonElement.ValueKind switch
                    {
                        JsonValueKind.String => jsonElement.GetString(),
                        JsonValueKind.Number => jsonElement.TryGetInt64(out long l) ? l : jsonElement.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Object => jsonElement.EnumerateObject().ToDictionary(kv => kv.Name, kv => kv.Value),
                        JsonValueKind.Array => jsonElement.EnumerateArray().ToList()
                    };
#pragma warning restore CS8601 // Possible null reference assignment.
                }

                // each item key may be encoded as an object path, so we need to traverse the object to find the field
                // for example: "Finances.BankBalance" would be deserialized.Finances.BankBalance
                string[] path = key.Split('.');
                object? current = deserializedData;

                for (int i = 0; i < path.Length - 1; i++)
                {
                    if (current == null)
                    {
                        break;
                    }

                    FieldInfo[] currentFields = current.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo? currentField = currentFields.FirstOrDefault(f => f.Name == path[i]);

                    if (currentField != null)
                    {
                        current = currentField.GetValue(current);
                    }
                    else
                    {
                        Console.WriteLine("Pathed field not found: " + path[i]);
                        return;
                    }
                }

                FieldInfo[] fields = current!.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? field = fields.FirstOrDefault(f => f.Name == path[^1]);

                if (field != null)
                {
                    field.SetValue(current, data[key]);
                }
                else
                {
                    Console.WriteLine("Field not found: " + path[^1]);
                    return;
                }
            }

            // serialize to a stream
            using MemoryStream memoryStream = new();
#pragma warning disable SYSLIB0011 // Type or member is obsolete
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.Serialize(memoryStream, deserializedData);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
            File.WriteAllBytes(options.InputFile, memoryStream.ToArray());

            Console.WriteLine("Overwrote save game data with values from: " + options.JSONFile);
        }
        else
        {
            Console.WriteLine("Failed to deserialize the input file. Serializer returned null.");
        }
    }

    private static void DumpDataFile()
    {
        if (options.MonoDLLPath == null)
        {
            Console.WriteLine("The Mono DLL path is required to deserialize the data file.");
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MonoAssemblyResolver);

        object? deserialized = Deserialize(File.ReadAllBytes(options.InputFile));

        if (deserialized == null)
        {
            Console.WriteLine("Failed to deserialize the data file. Serializer returned null.");
            return;
        }

        JsonConverter converter = deserialized switch
        {
            Life => new DataFileJSONConverter<Life>(options),
            _ => new DataFileJSONConverter<object>(options)
        };

        string outputFile = options.OutputFile ?? options.InputFile + ".json";
        string json = JsonSerializer.Serialize(deserialized, new JsonSerializerOptions
        {
            Converters = { converter },
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            IncludeFields = true
        });
        json = json.Replace("\\u003C", "<").Replace("\\u003E", ">");

        File.WriteAllText(outputFile, json);

        Console.WriteLine("Dumped data file to: " + outputFile);
    }

    public static void Main(string[] args)
    {
        options = Parser.Default.ParseArguments<BitLifeEditOptions>(args).Value;

        if (options == null)
        {
            return;
        }

        if (options.InputFile == null)
        {
            Console.WriteLine("No input file specified.");
            return;
        }

        if (options.Save) { DumpDataFile(); return; }

        if (options.Patch)
        {
            if (options.MonoDLLPath == null)
            {
                Console.WriteLine("The Mono DLL path is required to deserialize the data files.");
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MonoAssemblyResolver);
            object? deserialized = Deserialize(File.ReadAllBytes(options.InputFile));

            if (deserialized != null)
            {
                OverwriteDataFileValues(deserialized);
            }
            else
            {
                Console.WriteLine("Failed to deserialize the data file. Serializer returned null.");
                return;
            }
        }

        if (options.Decrypt)
        {
            byte[] fileHeader = File.ReadAllBytes(options.InputFile).Take(4).ToArray();

            // check if this is a data file
            if (fileHeader.SequenceEqual(saveGameHeader))
            {
                Console.WriteLine("Auto-detected a save game file. Actions will be limited to dumping only.");

                DumpDataFile();

                return;
            }

            DecryptVarFile();

            return;
        }

        if (options.Encrypt) { EncryptVarFile(); return; }
    }
}

public class DataFileJSONConverter<T> : JsonConverter<T>
{
    private readonly Program.BitLifeEditOptions options;

    public DataFileJSONConverter(Program.BitLifeEditOptions options)
    {
        this.options = options;
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var props = this.Collect(value);
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true
        };
        var ser = JsonSerializer.Serialize(props, opts);

        writer.WriteRawValue(ser);
    }

    // recursively add all fields to a dictionary and return it
    private Dictionary<string, object?> Collect(T obj)
    {
        var stack = new Stack<(object obj, int depth, Dictionary<string, object?> container, string key)>();
        var root = new Dictionary<string, object?>();

        // keep a log of already visited objects to prevent infinite loops
        var visited = new HashSet<object>();

        stack.Push(((object)obj!, 0, root, obj.GetType().Name));
        visited.Add(obj);

        while (stack.Count > 0)
        {
            var (currentObj, currentDepth, currentContainer, currentKey) = stack.Pop();

            if (this.options.MaxDepth != 0 && currentDepth > this.options.MaxDepth)
            {
                currentContainer[currentKey] = new Dictionary<string, object> { { "MAXIMUM DEPTH REACHED", this.options.MaxDepth } };

                continue;
            }

            var fields = currentObj.GetType()
                                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 .ToDictionary(x => x.Name, x => x.GetValue(currentObj));
            var fieldContainer = currentKey == null ? currentContainer : new Dictionary<string, object?>();

            if (currentKey != null) currentContainer[currentKey] = fieldContainer;

            foreach (var field in fields)
            {
                if (field.Value != null)
                {
                    if (field.Value is not IEnumerable && field.Value.GetType().IsClass && field.Value.GetType() != typeof(string))
                    {
                        if (visited.Contains(field.Value))
                        {
                            fieldContainer[field.Key] = new Dictionary<string, object> { { "CIRCULAR REFERENCE", field.Value.GetType().Name } };
                            continue;
                        }

                        stack.Push((field.Value, currentDepth + 1, fieldContainer, field.Key));
                        visited.Add(field.Value);
                    }
                    else if (field.Value is IEnumerable enumerable && !(field.Value is string))
                    {
                        List<object?> list = new();
                        int index = 0;

                        foreach (var item in enumerable)
                        {
                            if (item == null)
                            {
                                list.Add(null);
                                continue;
                            }

                            if (item is string || !item.GetType().IsClass)
                            {
                                list.Add(item);
                                continue;
                            }

                            var listContainer = new Dictionary<string, object?>();

                            list.Add(listContainer);

                            if (visited.Contains(item))
                            {
                                listContainer["CIRCULAR REFERENCE"] = item.GetType().Name;
                                continue;
                            }

                            stack.Push((item, currentDepth + 1, listContainer, index.ToString()));
                            visited.Add(item);

                            index++;
                        }

                        fieldContainer[field.Key] = list;
                    }
                    else
                    {
                        fieldContainer[field.Key] = field.Value;
                    }
                }
            }
        }

        return root;
    }
}
