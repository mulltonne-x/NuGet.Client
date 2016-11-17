// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NuGet.VisualStudio.Workspace.Extensions.Implementation
{
    /// <summary>
    /// Helper class for working with files with Json content
    /// </summary>
    internal sealed class JsonHelper
    {
        /// <summary>
        /// Deserialize a file containing json into a JObject
        /// </summary>
        /// <param name="jsonFilePath">Json file path</param>
        /// <returns>JObject result</returns>
        internal static JObject DeserializeObjectFromFilePath(string jsonFilePath)
        {
            using (TextReader tr = new StreamReader(new FileStream(jsonFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                using (JsonReader jr = new JsonTextReader(tr))
                {
                    return (JObject)JToken.ReadFrom(jr);
                }
            }
        }

        /// <summary>
        /// Deserializes a <see cref="JObject"/> into key-value pairs
        /// </summary>
        /// <param name="jObject">Json object to deserialize</param>
        /// <returns>Dictionary of key/value pairs</returns>
        internal static IDictionary<string, object> DeserializeJObject(JObject jObject)
        {
            var values = new Dictionary<string, object>();

            foreach (KeyValuePair<string, JToken> kvp in jObject)
            {
                values.Add(kvp.Key, UnboxToken(kvp.Value));
            }

            return values;
        }

        private static object UnboxToken(JToken jToken)
        {
            if (jToken is JObject)
            {
                return DeserializeJObject((JObject)jToken);
            }
            else if (jToken is JArray)
            {
                var arrayList = new List<object>();
                foreach (var item in (JArray)jToken)
                {
                    arrayList.Add(UnboxToken(item));
                }

                return arrayList.ToArray();
            }
            else
            {
                JTokenType tokenType = jToken.Type;
                object o = null;
                switch (tokenType)
                {
                    case JTokenType.Boolean:
                        o = jToken.ToObject<bool>();
                        break;
                    case JTokenType.Integer:
                        o = jToken.ToObject<int>();
                        break;
                    case JTokenType.Date:
                        o = jToken.ToObject<DateTime>();
                        break;
                    case JTokenType.Float:
                        o = jToken.ToObject<float>();
                        break;
                    case JTokenType.Guid:
                        o = jToken.ToObject<Guid>();
                        break;
                    case JTokenType.String:
                        o = jToken.ToObject<string>();
                        break;
                }

                return o;
            }
        }
    }
}
