namespace ShellCommand.Broker;
public static class DefaultConfiguration
{
    public const string Text = """
        version: 2
        menu:
          - id: terminal
            title: 在此打开终端
            icon: terminal
            run:
              exe: wt.exe
              args: ['-d', '${directory}']

          - id: copy-directory
            title: 复制目录路径
            icon: copy
            copy: '${directory}'

          - id: selected
            title: 选中文件
            icon: file
            when:
              context: selection
            items:
              - id: copy-selection
                title: 复制所选路径
                icon: copy
                copy:
                  values: '${selection.paths}'
                  separator: "\r\n"
        """;
}
