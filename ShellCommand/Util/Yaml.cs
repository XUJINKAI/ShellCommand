using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShellCommand.Util
{
    public static class Yaml
    {
        public static void SaveYaml<T>(string path, T obj)
        {
            var yaml = new YamlDotNet.Serialization.Serializer();
            var contents = yaml.Serialize(obj);
            File.WriteAllText(path, Env.ConfigTitle + contents);
        }

        public static T LoadYaml<T>(string path)
        {
            var text = File.ReadAllText(path);
            var yaml = new YamlDotNet.Serialization.Deserializer();
            var o = yaml.Deserialize<T>(text);
            return o;
        }
    }
}
