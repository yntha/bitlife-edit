# bitlife-edit

A simple tool to edit/dump BitLife save files or var files.

## Installation

Download the latest release from the [releases page](https://github.com/yntha/bitlife-edit/releases) or [build](#building) the project yourself. <b>NOTE!!!:</b> You will need to rebuild the project with the Mono DLLs from that game version to ensure compatibility or else you will run into deserialization issues.

## Usage
#### The Save Editor
bitlife-edit comes with a REPL that allows you to edit save files interactively. To start it, run:
```sh
bitlife-edit.exe -r -m <mono dll path> <saveFile.data>
```
The REPL also launches automatically if you run the program without any arguments:
```sh
bitlife-edit.exe -m <mono dll path> <saveFile.data>
==== BitLife Save Editor REPL ====
Type 'help' for available commands or 'quit' to exit.
Example: set money 99999

bitlife>
```
Run the `help` command to see a list of available commands:
```sh
bitlife> help
Available commands:
  set <field> <value> - Set a field to a specific value
  get <field>         - Get the current value of a field
  show                - Display current character stats
  save [filename]     - Save changes to file
  help                - Show this help message
  quit/exit           - Exit the REPL

Supported fields:
  money, cash, bank, balance - Character's money
  age                        - Character's age

Examples:
  set money 99999     - Set money to 99,999
  get money           - Get current money value
  set age 30          - Set age to 30
  show                - Display all stats
```

#### Dumping a save file
```sh
bitlife-edit.exe -s saveFile.data -m dump/DummyDll
```

You can also dump any other file that is serialized in the same format as a save file. Typically, this means any .data file:

```sh
bitlife-edit.exe -s graves.data -m dump/DummyDll
```

Note: Doing this requires the Mono assemblies for the game. To obtain them, you can use [Il2CppDumper](https://github.com/Perfare/Il2CppDumper) to generate dummy dlls. Once obtained, specify the path to the Mono DLLs using the `-m/--mono` flag.


#### Editing a save file
```sh
bitlife-edit.exe -p -f fields.json saveFile.data
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
$ bitlife-edit.exe --patch -f patch.json saveFile.data
```

You can obtain the field names by dumping the save file first with the `-s/--save` flag.

#### Decrypting a var file
```sh
bitlife-edit.exe -d MonetizationVars
```

#### Encrypting a var file
```sh
bitlife-edit.exe -e moneyvars.json
```

In this case, the program will write the encrypted var file to `moneyvars.json.var`.

#### All options
```
bitlife-edit 1.2.1+a1fa0f140879c34cc269a505efa2695eb0ac6c91
Copyright (C) 2024 bitlife-edit

  -o, --output      The output file to write the JSON data to.

  -m, --mono        The path to the Mono DLL files.

  -d, --decrypt     Decrypt the input var file.

  -e, --encrypt     Encrypt the input var file.

  -c, --cipher      Overrides the default cipher key used to decrypt var files.

  -s, --save        Dump a .data file to a JSON file.

  -p, --patch       Patch any .data file based on variables stored in a JSON file.

  -f, --file        The JSON file containing the variables required for a patch.

  --max_depth       (Default: 0) The maximum depth to traverse when serializing the save game data. Default is 0 (no limit).

  --help            Display this help screen.

  --version         Display version information.

  input (pos. 0)    Required. The input file to process.
```

## Building
To build the project, you will need to have [.NET Framework 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) installed.
It is recommended you use Visual Studio to build the project.

Furthermore, you will need to add the game's Mono assembly, "Assembly-CSharp", as a dependency of the project. You can obtain this DLL by using [Il2CppDumper](https://github.com/Perfare/Il2CppDumper) to generate dummy DLLs.
