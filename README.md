# bitlife-edit

A simple tool to edit/dump BitLife save files or var files.

## Installation

Download the latest release from the [releases page](https://github.com/yntha/bitlife-edit/releases) or [build](#building) the project yourself.

## Usage
#### Dumping a save file
```sh
bitlife-edit.exe -s saveFile.data
```

Note: Doing this requires the Mono assemblies for the game. To obtain them, you can use [Il2CppDumper](https://github.com/Perfare/Il2CppDumper) to generate dummy dlls. Once obtained, specify the path to the Mono DLLs using the `-m/--mono` flag.


#### Editing a save file
```sh
bitlife-edit.exe -l -f fields.json saveFile.data
```

`fields.json` may be any JSON file with the following format:
```json
{
	"field1": "value1",
	"field2": "value2",
	...
}
```

It must contain the fields you want to edit and their new values. For example:
```json
{
	"m_name": "John Doe",
	"m_age": 34
}
```

If you want to edit a field that exists within a nested object, you can use the dot notation. For example:
```json
{
	"m_currentJob.m_jobTitle": "Doctor"
	"m_currentJob.m_Finances.m_BankBalance": 100000
}
```

Here is a real-world example of me patching my bank balance:
```sh
$ cat patch.json
{
    "<Finances>k__BackingField.<BankBalance>k__BackingField": 9999999999.0
}
$ bitlife-edit.exe -l -f patch.json saveFile.data
```

You can obtain the field names by dumping the save file first with the `-s/--save` flag.

#### Dumping a var file
```sh
bitlife-edit.exe -d MonetizationVars
```

#### Editing a var file
```sh
bitlife-edit.exe -l -f moneyvars.json MonetizationVars
```

In this case, the program will write the modified vars to `MonetizationVars`.

#### All options
```
bitlife-edit 1.0.0+5578a7d3e2c2d06bc692f10158e041d54939b7a6
Copyright (C) 2024 bitlife-edit

  -o, --output      The output file to write the JSON data to.

  -m, --mono        The path to the Mono DLL files.

  -d, --decrypt     Decrypt the input file.

  -e, --encrypt     Encrypt the input file.

  -c, --cipher      Overrides the default cipher key used to decrypt var files.

  -s, --save        Dump the save game data to a JSON file.

  -l, --load        Overwrite save game data based on variables stored in a JSON file.

  -f, --file        The JSON file to load data from.

  --max_depth       (Default: 0) The maximum depth to traverse when serializing the save game data. Default is 0 (no
                    limit).

  --help            Display this help screen.

  --version         Display version information.

  input (pos. 0)    Required. The input file to process.
```

## Building
To build the project, you will need to have [.NET Framework 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) installed.
It is recommended you use Visual Studio to build the project.

Furthermore, you will need to add the game's Mono assembly, "Assembly-CSharp", as a dependency of the project. You can obtain this DLL by using [Il2CppDumper](https://github.com/Perfare/Il2CppDumper) to generate dummy DLLs.
