Get-ChildItem -Path .\ShellCommand\bin\x64\Release\* -recurse -exclude *.pdb | Compress-Archive -DestinationPath ShellCommand.x64.zip
Get-ChildItem -Path .\ShellCommand\bin\x86\Release\* -recurse -exclude *.pdb | Compress-Archive -DestinationPath ShellCommand.x86.zip
