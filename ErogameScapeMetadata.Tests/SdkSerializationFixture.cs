using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Playnite.SDK.Data;
using System;
using System.IO;
using System.Reflection;

namespace ErogameScapeMetadata.Tests
{
    /// <summary>
    /// Playnite.SDK.Data.Serialization は Playnite 本体が起動時に注入する
    /// IDataSerializer への facade でしかない（内部 Serialization.Init）。
    /// テストプロセスでは注入されないので、同じ規約（SerializationPropertyName）で
    /// 動く Newtonsoft 実装を差し込む。
    /// </summary>
    internal static class SdkSerialization
    {
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static void Ensure()
        {
            lock (Sync)
            {
                if (_initialized)
                {
                    return;
                }

                var init = typeof(Serialization).GetMethod(
                    "Init", BindingFlags.NonPublic | BindingFlags.Static);
                if (init == null)
                {
                    throw new InvalidOperationException(
                        "Playnite.SDK Serialization.Init が見つからない（SDK のバージョン差異）");
                }

                init.Invoke(null, new object[] { new TestDataSerializer() });
                _initialized = true;
            }
        }
    }

    /// <summary>Serialization を使うテストクラスはこれを継承する。</summary>
    public abstract class SerializationTestBase
    {
        protected SerializationTestBase()
        {
            SdkSerialization.Ensure();
        }
    }

    internal class SdkContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(
            MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            var attribute = member.GetCustomAttribute<SerializationPropertyNameAttribute>();
            if (attribute != null && !string.IsNullOrEmpty(attribute.PropertyName))
            {
                property.PropertyName = attribute.PropertyName;
            }
            return property;
        }
    }

    // JSON のみ実装。YAML/TOML/ファイル系はテストで使わないため未実装。
    internal class TestDataSerializer : IDataSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new SdkContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public string ToJson(object obj, bool formatted = false)
        {
            return JsonConvert.SerializeObject(
                obj, formatted ? Formatting.Indented : Formatting.None, Settings);
        }

        public T FromJson<T>(string json) where T : class
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public bool TryFromJson<T>(string json, out T content) where T : class
        {
            Exception error;
            return TryFromJson(json, out content, out error);
        }

        public bool TryFromJson<T>(string json, out T content, out Exception error) where T : class
        {
            try
            {
                content = FromJson<T>(json);
                error = null;
                return content != null;
            }
            catch (Exception ex)
            {
                content = null;
                error = ex;
                return false;
            }
        }

        public void ToJsonStream(object obj, Stream stream, bool formatted = false)
        {
            throw new NotImplementedException();
        }

        public T FromJsonStream<T>(Stream stream) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromJsonStream<T>(Stream stream, out T content) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromJsonStream<T>(Stream stream, out T content, out Exception error) where T : class
        {
            throw new NotImplementedException();
        }

        public T FromJsonFile<T>(string filePath) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromJsonFile<T>(string filePath, out T content) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromJsonFile<T>(string filePath, out T content, out Exception error) where T : class
        {
            throw new NotImplementedException();
        }

        public string ToYaml(object obj)
        {
            throw new NotImplementedException();
        }

        public T FromYaml<T>(string yaml) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromYaml<T>(string yaml, out T content) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromYaml<T>(string yaml, out T content, out Exception error) where T : class
        {
            throw new NotImplementedException();
        }

        public T FromYamlFile<T>(string filePath) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromYamlFile<T>(string filePath, out T content) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromYamlFile<T>(string filePath, out T content, out Exception error) where T : class
        {
            throw new NotImplementedException();
        }

        public T FromToml<T>(string toml) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromToml<T>(string toml, out T content) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromToml<T>(string toml, out T content, out Exception error) where T : class
        {
            throw new NotImplementedException();
        }

        public T FromTomlFile<T>(string filePath) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromTomlFile<T>(string filePath, out T content) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryFromTomlFile<T>(string filePath, out T content, out Exception error) where T : class
        {
            throw new NotImplementedException();
        }

        public T GetClone<T>(T source) where T : class
        {
            return FromJson<T>(ToJson(source));
        }

        public U GetClone<T, U>(T source)
            where T : class
            where U : class
        {
            return FromJson<U>(ToJson(source));
        }

        public bool AreObjectsEqual(object object1, object object2)
        {
            return ToJson(object1) == ToJson(object2);
        }
    }
}
