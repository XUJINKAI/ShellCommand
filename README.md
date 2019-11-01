# ShellCommand

Customize your context menu.

![screenshot](/docs/screenshot.png)

## Feature

- Custom one folder's context menu by `.shellcommand.yaml`
- Custom global context menu by `global.shellcommand.yaml`
- Support Environment variable
- Support Wildcard syntax !?* Match
- Support Menu Item Icon

## Usage

Open ShellCommand.exe, Click Install, Bingo!

### Command

Support [all windows variables](https://pureinfotech.com/list-environment-variables-windows-10/) like `%LocalAppData%`,

Plus, `%DIR%` stands for current folder.

### Match

- If not null, checks if current folder have the name (file or directory)
- Splits conditions by **<&&>**
- Starts by **!** for reverse condition
- Use **?** and ***** for wildcard

### Icon

- Exe associated icon
- Dll resource, use `?index` for index number
    e.g. `%SystemRoot%\System32\Shell32.dll?3`

### Name

- `---` for separator
- If ignored, command text will be used.

## Known Issues

- After Uninstall, explorer.exe keep loading program. You should restart explorer to release or the file cannot be deleted.

## LICENSE

MIT License.
