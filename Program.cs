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
using System.Runtime.Serialization;

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

        [Option('r', "repl", Required = false, HelpText = "Start interactive REPL mode for editing save files.")]
        public bool Repl { get; set; }
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
    private static BitLifeEditOptions? options;

    private static object? Deserialize(byte[] inputData)
    {
        object? deserialized = null;

        try
        {
            using MemoryStream memoryStream = new(inputData);

#pragma warning disable SYSLIB0011
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.SurrogateSelector = new PermissiveSurrogateSelector();
            binaryFormatter.Binder = new PermissiveSerializationBinder();
#pragma warning restore SYSLIB0011
            deserialized = binaryFormatter.Deserialize(memoryStream);
        }
        catch (SerializationException ex) when (ex.InnerException is InvalidCastException)
        {
            Console.WriteLine("Serialization error encountered. Attempting fallback deserialization...");

            try
            {
                deserialized = DeserializeWithPermissiveSettings(inputData);
            }
            catch (Exception fallbackEx)
            {
                Console.WriteLine($"Fallback deserialization also failed: {fallbackEx.Message}");
                throw;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Deserializer Error: " + e.Message);
            throw;
        }

        return deserialized;
    }

    private static object? DeserializeWithPermissiveSettings(byte[] inputData)
    {
        using MemoryStream memoryStream = new(inputData);

#pragma warning disable SYSLIB0011
        BinaryFormatter binaryFormatter = new();
        binaryFormatter.SurrogateSelector = new DebuggingSurrogateSelector();
        binaryFormatter.Binder = new PermissiveSerializationBinder();
#pragma warning restore SYSLIB0011

        return binaryFormatter.Deserialize(memoryStream);
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

    private static object ConvertJsonElement(JsonElement jsonElement)
    {
#pragma warning disable CS8603 // Possible null reference return.
        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement.TryGetInt64(out long l) ? l : jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => jsonElement.EnumerateObject().ToDictionary(kv => kv.Name, kv => ConvertJsonElement(kv.Value)),
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(ConvertJsonElement).ToList(),
            _ => null
        };
#pragma warning restore CS8603 // Possible null reference return.
    }

    // encrypt the json file in inputFile to a var file
    private static void EncryptVarFile()
    {
        if (options == null) return;

        string json = File.ReadAllText(options.InputFile!);
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
                itemMap[key] = ConvertJsonElement(jsonElement);
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
        if (options == null) return;

        string[] fileLines = File.ReadAllLines(options.InputFile!);
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
        if (options == null) throw new InvalidOperationException("Options not initialized");

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
        if (options?.MonoDLLPath == null)
        {
            Console.WriteLine("The Mono DLL path is required to deserialize the save game data.");
            return null;
        }

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MonoAssemblyResolver);
        return (Life?)Deserialize(File.ReadAllBytes(inputFile));
    }

    private static void OverwriteDataFileValues(object deserializedData)
    {
        if (options == null) return;

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
                    data[key] = ConvertJsonElement(jsonElement);
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
            File.WriteAllBytes(options.InputFile!, memoryStream.ToArray());

            Console.WriteLine("Overwrote save game data with values from: " + options.JSONFile);
        }
        else
        {
            Console.WriteLine("Failed to deserialize the input file. Serializer returned null.");
        }
    }

    private static void DumpDataFile()
    {
        if (options?.MonoDLLPath == null)
        {
            Console.WriteLine("The Mono DLL path is required to deserialize the data file.");
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MonoAssemblyResolver);

        object? deserialized = Deserialize(File.ReadAllBytes(options.InputFile!));

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

    private static void StartRepl()
    {
        if (options?.MonoDLLPath == null)
        {
            Console.WriteLine("The Mono DLL path is required for REPL mode.");
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MonoAssemblyResolver);

        object? saveData = Deserialize(File.ReadAllBytes(options.InputFile!));
        if (saveData == null)
        {
            Console.WriteLine("Failed to load save file.");
            return;
        }

        var repl = new BitLifeRepl((Life) saveData, options);
        repl.Run();
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

        // launch repl if no other options are set
        if (!options.Save && !options.Patch && !options.Decrypt && !options.Encrypt)
        {
            StartRepl();
        }
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
        var circularReferenceCount = new Dictionary<object, int>();
        const int maxCircularReferences = 100;

        stack.Push(((object)obj!, 0, root, obj!.GetType().Name));
        visited.Add(obj!);

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
                            circularReferenceCount.TryGetValue(field.Value, out int count);
                            count++;
                            circularReferenceCount[field.Value] = count;

                            if (count <= maxCircularReferences)
                            {
                                stack.Push((field.Value, currentDepth + 1, fieldContainer, field.Key));
                            }
                            else
                            {
                                // halt further traversal and indicate circular reference
                                fieldContainer[field.Key] = new Dictionary<string, object> { { "CIRCULAR_REFERENCE", field.Value.GetType().Name } };
                            }
                            continue;
                        }

                        stack.Push((field.Value, currentDepth + 1, fieldContainer, field.Key));
                        visited.Add(field.Value);
                        circularReferenceCount[field.Value] = 0;
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
                                circularReferenceCount.TryGetValue(item, out int count);
                                count++;
                                circularReferenceCount[item] = count;

                                if (count <= maxCircularReferences)
                                {
                                    stack.Push((item, currentDepth + 1, listContainer, index.ToString()));
                                }
                                else
                                {
                                    listContainer["CIRCULAR_REFERENCE"] = item.GetType().Name;
                                }
                                index++;
                                continue;
                            }

                            stack.Push((item, currentDepth + 1, listContainer, index.ToString()));
                            visited.Add(item);
                            circularReferenceCount[item] = 0;

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

public class BitLifeRepl
{
    private readonly Life saveData;
    private readonly Program.BitLifeEditOptions options;
    private readonly Dictionary<string, IReplCommand> commands;
    private bool isRunning = true;

    public BitLifeRepl(Life saveData, Program.BitLifeEditOptions options)
    {
        this.saveData = saveData;
        this.options = options;
        this.commands = new Dictionary<string, IReplCommand>();

        RegisterCommands();
    }

    private void RegisterCommands()
    {
        commands["set"] = new SetCommand();
        commands["get"] = new GetCommand();
        commands["show"] = new ShowCommand();
        commands["help"] = new HelpCommand();
        commands["save"] = new SaveCommand();
        commands["quit"] = new QuitCommand();
        commands["exit"] = new QuitCommand();
    }

    public void Run()
    {
        Console.WriteLine("==== BitLife Save Editor REPL ====");
        Console.WriteLine("Type 'help' for available commands or 'quit' to exit.");
        Console.WriteLine("Example: set money 99999");
        Console.WriteLine();

        while (isRunning)
        {
            Console.Write("bitlife> ");
            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            ProcessCommand(input);
        }
    }

    private void ProcessCommand(string input)
    {
        try
        {
            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string commandName = parts[0].ToLower();
            string[] args = parts.Skip(1).ToArray();

            if (commands.TryGetValue(commandName, out IReplCommand? command))
            {
                var context = new ReplContext(saveData, options, this);
                command.Execute(context, args);
            }
            else
            {
                Console.WriteLine($"Unknown command: {commandName}. Type 'help' for available commands.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing command: {ex.Message}");
        }
    }

    public void Stop()
    {
        isRunning = false;
    }
}

public class ReplContext
{
    public Life SaveData { get; }
    public Program.BitLifeEditOptions Options { get; }
    public BitLifeRepl Repl { get; }
    private readonly List<IFieldHandler> fieldHandlers;

    public ReplContext(Life saveData, Program.BitLifeEditOptions options, BitLifeRepl repl)
    {
        SaveData = saveData;
        Options = options;
        Repl = repl;

        // init field handlers
        fieldHandlers = new List<IFieldHandler>
        {
            new MoneyFieldHandler()
        };
    }

    public bool SetFieldByHandler(string fieldName, object value)
    {
        foreach (var handler in fieldHandlers)
        {
            if (handler.SupportedFields.Contains(fieldName.ToLower()))
            {
                return handler.TrySetField(this, fieldName, value);
            }
        }
        return false;
    }

    public bool GetFieldByHandler(string fieldName, out object? value)
    {
        foreach (var handler in fieldHandlers)
        {
            if (handler.SupportedFields.Contains(fieldName.ToLower()))
            {
                return handler.TryGetField(this, fieldName, out value);
            }
        }
        value = null;
        return false;
    }

    public string[] GetSupportedFields()
    {
        return fieldHandlers.SelectMany(h => h.SupportedFields).ToArray();
    }

    public bool SetField(string fieldPath, object value)
    {
        try
        {
            string[] path = fieldPath.Split('.');
            object? current = SaveData;

            // nav to parent object
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (current == null) return false;

                FieldInfo[] currentFields = current.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? currentField = currentFields.FirstOrDefault(f => f.Name == path[i]);

                if (currentField != null)
                {
                    current = currentField.GetValue(current);
                }
                else
                {
                    currentField = currentFields.FirstOrDefault(f => f.Name.Contains(path[i], StringComparison.OrdinalIgnoreCase));
                    if (currentField != null)
                    {
                        current = currentField.GetValue(current);
                    }
                    else
                    {
                        Console.WriteLine($"Field not found: {path[i]}");
                        return false;
                    }
                }
            }

            // Set the final field
            if (current != null)
            {
                FieldInfo[] fields = current.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? field = fields.FirstOrDefault(f => f.Name == path[^1]);

                if (field == null)
                {
                    field = fields.FirstOrDefault(f => f.Name.Contains(path[^1], StringComparison.OrdinalIgnoreCase));
                }

                if (field != null)
                {
                    // convert value to the correct type
                    object convertedValue = Convert.ChangeType(value, field.FieldType);
                    field.SetValue(current, convertedValue);
                    return true;
                }
                else
                {
                    Console.WriteLine($"Field not found: {path[^1]}");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting field: {ex.Message}");
        }

        return false;
    }

    public object? GetField(string fieldPath)
    {
        try
        {
            string[] path = fieldPath.Split('.');
            object? current = SaveData;

            foreach (string component in path)
            {
                if (current == null) return null;

                FieldInfo[] fields = current.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? field = fields.FirstOrDefault(f => f.Name == component);

                if (field == null)
                {
                    field = fields.FirstOrDefault(f => f.Name.Contains(component, StringComparison.OrdinalIgnoreCase));
                }

                if (field != null)
                {
                    current = field.GetValue(current);
                }
                else
                {
                    return null;
                }
            }

            return current;
        }
        catch
        {
            return null;
        }
    }
}

public interface IFieldHandler
{
    string[] SupportedFields { get; }
    bool TryGetField(ReplContext context, string fieldName, out object? value);
    bool TrySetField(ReplContext context, string fieldName, object value);
    string GetDescription(string fieldName);
}

public class MoneyFieldHandler : IFieldHandler
{
    public static readonly string FieldName = "<Finances>k__BackingField.<BankBalance>k__BackingField";
    public string[] SupportedFields => new[] { "money", "cash", "bank", "balance" };

    public bool TryGetField(ReplContext context, string fieldName, out object? value)
    {
        value = context.GetField(FieldName);
        return value != null;
    }

    public bool TrySetField(ReplContext context, string fieldName, object value)
    {
        return context.SetField(FieldName, value);
    }

    public string GetDescription(string fieldName)
    {
        return "Character's money/bank balance";
    }
}

public interface IReplCommand
{
    void Execute(ReplContext context, string[] args);
    string GetHelp();
}

public class SetCommand : IReplCommand
{
    public void Execute(ReplContext context, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: set <field> <value>");
            Console.WriteLine("Examples:");
            Console.WriteLine("  set money 99999");
            return;
        }

        string fieldName = args[0].ToLower();
        string valueStr = args[1];

        if (TryParseValue(valueStr, out object? value) && value != null)
        {
            if (context.SetFieldByHandler(fieldName, value))
            {
                Console.WriteLine($"Set {fieldName} to: {value}");
            }
            else
            {
                Console.WriteLine($"Could not find or set field: {fieldName}");
                Console.WriteLine("Try 'help' to see supported fields.");
            }
        }
        else
        {
            Console.WriteLine($"Invalid value: {valueStr}");
        }
    }

    private bool TryParseValue(string valueStr, out object? value)
    {
        value = null;

        // Try parsing as different types
        if (long.TryParse(valueStr, out long longValue))
        {
            value = longValue;
            return true;
        }

        if (double.TryParse(valueStr, out double doubleValue))
        {
            value = doubleValue;
            return true;
        }

        if (bool.TryParse(valueStr, out bool boolValue))
        {
            value = boolValue;
            return true;
        }

        // Default to string
        value = valueStr;
        return true;
    }

    public string GetHelp() => "set <field> <value> - Set a field to a specific value";
}

public class GetCommand : IReplCommand
{
    public void Execute(ReplContext context, string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: get <field>");
            Console.WriteLine("Examples:");
            Console.WriteLine("  get money");
            return;
        }

        string fieldName = args[0].ToLower();

        if (context.GetFieldByHandler(fieldName, out object? value))
        {
            Console.WriteLine($"{fieldName}: {value}");
        }
        else
        {
            Console.WriteLine($"Field '{fieldName}' not found or has no value.");
            Console.WriteLine("Try 'help' to see supported fields.");
        }
    }

    public string GetHelp() => "get <field> - Get the current value of a field";
}

public class ShowCommand : IReplCommand
{
    public void Execute(ReplContext context, string[] args)
    {
        Console.WriteLine("=== Current Character Stats ===");

        string[] statsToShow = {
            MoneyFieldHandler.FieldName,
        };

        foreach (string stat in statsToShow)
        {
            var value = context.GetField(stat);
            if (value != null)
            {
                Console.WriteLine($"{stat}: {value}");
            }
        }
    }

    public string GetHelp() => "show - Display current character statistics";
}

public class SaveCommand : IReplCommand
{    public void Execute(ReplContext context, string[] args)
    {
        try
        {
            using MemoryStream memoryStream = new();
#pragma warning disable SYSLIB0011
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.Serialize(memoryStream, context.SaveData);
#pragma warning restore SYSLIB0011

            string outputFile = args.Length > 0 ? args[0] : context.Options.InputFile!;
            File.WriteAllBytes(outputFile, memoryStream.ToArray());

            Console.WriteLine($"Save file written to: {outputFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving file: {ex.Message}");
        }
    }

    public string GetHelp() => "save [filename] - Save changes to file";
}

public class HelpCommand : IReplCommand
{
    public void Execute(ReplContext context, string[] args)
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("  set <field> <value> - Set a field to a specific value");
        Console.WriteLine("  get <field>         - Get the current value of a field");
        Console.WriteLine("  show                - Display current character stats");
        Console.WriteLine("  save [filename]     - Save changes to file");
        Console.WriteLine("  help                - Show this help message");
        Console.WriteLine("  quit/exit           - Exit the REPL");
        Console.WriteLine();
        Console.WriteLine("Supported fields:");
        Console.WriteLine("  money, cash, bank, balance - Character's money");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  set money 99999     - Set money to 99,999");
        Console.WriteLine("  show                - Display all stats");
    }

    public string GetHelp() => "help - Show available commands";
}

public class QuitCommand : IReplCommand
{
    public void Execute(ReplContext context, string[] args)
    {
        Console.WriteLine("Exiting REPL...");
        context.Repl.Stop();
    }

    public string GetHelp() => "quit - Exit the REPL";
}

#pragma warning disable SYSLIB0050 // Type or member is obsolete
public class LifeSerializationSurrogate : ISerializationSurrogate
{
    public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
    {
        throw new NotImplementedException();
    }

    public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
    {
        var life = (Life)obj;
        var fields = typeof(Life).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var field in fields)
        {
            try
            {
                var value = info.GetValue(field.Name, field.FieldType);
                field.SetValue(life, value);
            }
            catch (InvalidCastException ex) when (ex.Message.Contains("IConvertible"))
            {
                Console.WriteLine($"Skipping problematic field: {field.Name} of type {field.FieldType}");

                if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    HandleProblematicDictionary(life, field, info);
                }
                else
                {
                    field.SetValue(life, GetDefaultValue(field.FieldType));
                }
            }
            catch (Exception ex)
            {
                field.SetValue(life, GetDefaultValue(field.FieldType));
            }
        }

        return life;
    }

    private void HandleProblematicDictionary(Life life, FieldInfo field, SerializationInfo info)
    {
        try
        {
            var dictType = field.FieldType;
            var emptyDict = Activator.CreateInstance(dictType);
            field.SetValue(life, emptyDict);

            Console.WriteLine($"Set {field.Name} to empty dictionary due to serialization issues");
        }
        catch (Exception ex)
        {
            field.SetValue(life, null);
        }
    }

    private object? GetDefaultValue(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }
        return null;
    }
}

// Generic dictionary surrogate for other problematic dictionaries
public class GenericDictionarySurrogate : ISerializationSurrogate
{
    public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
    {
        throw new NotImplementedException();
    }

    public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
    {
        var dictType = obj.GetType();

        try
        {
            var emptyDict = Activator.CreateInstance(dictType);
            return emptyDict ?? obj;
        }
        catch
        {
            return obj;
        }
    }
}

// permissive surrogate selector for fallback deserialization
public class PermissiveSurrogateSelector : ISurrogateSelector
{
    public void ChainSelector(ISurrogateSelector selector) { }
    public ISurrogateSelector? GetNextSelector() => null;    public ISerializationSurrogate? GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
    {
        selector = null!;

        if (type == typeof(Life))
        {
            return new LifeSerializationSurrogate();
        }

        // using a generic surrogate for all dictionary types to avoid IConvertible issues
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            return new GenericDictionarySurrogate();
        }

        return null;
    }
}

public class PermissiveSerializationBinder : SerializationBinder
{
    public override Type? BindToType(string assemblyName, string typeName)
    {
        // Console.WriteLine($"Binding: {assemblyName} -> {typeName}");
        return null;
    }
}

public class DebuggingSurrogateSelector : ISurrogateSelector
{
    public void ChainSelector(ISurrogateSelector selector) { }
    public ISurrogateSelector? GetNextSelector() => null;    public ISerializationSurrogate? GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
    {
        selector = null!;
        //Console.WriteLine($"Deserializing type: {type.FullName}");

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keyType = type.GetGenericArguments()[0];
            var valueType = type.GetGenericArguments()[1];
            // Console.WriteLine($"  Dictionary<{keyType.FullName}, {valueType.FullName}>");
            // Console.WriteLine($"  Key type implements IConvertible: {typeof(IConvertible).IsAssignableFrom(keyType)}");
            // Console.WriteLine($"  Value type implements IConvertible: {typeof(IConvertible).IsAssignableFrom(valueType)}");
        }

        return null;
    }
}
#pragma warning restore SYSLIB0050
