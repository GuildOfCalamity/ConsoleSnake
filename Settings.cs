#region [Includes]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Reflection;
#endregion [Includes]

namespace Snake
{
    /// <summary>
    /// This is our serializable class for storing app settings.
    /// </summary>
    /// 
    [DataContract]
    public class Settings
    {
        private string _FontName = "Consolas";
        private string _FontSize = "32";

        public Settings(string fontName, string fontSize)
        {
            _FontName = fontName;
            _FontSize = fontSize;
        }

        [DataMember]
        public string FontName
        {
            get { return _FontName; }
            set { _FontName = value; }
        }

        [DataMember]
        public string FontSize
        {
            get { return _FontSize; }
            set { _FontSize = value; }
        }

        /// <summary>
        /// We can use our old friend Reflection to iterate through internal class members/properties.
        /// </summary>
        public IEnumerable<object> ListSettings()
        {
            FieldInfo[] fields = typeof(Settings).GetFields(BindingFlags.Instance |
                                                            BindingFlags.Static |
                                                            BindingFlags.Public |
                                                            BindingFlags.NonPublic |
                                                            BindingFlags.FlattenHierarchy);
            foreach (FieldInfo field in fields)
            {
                if (field.IsStatic)
                    yield return field.GetValue(field);
                else
                    yield return GetInstanceField(typeof(Settings), this, field.Name);
            }
        }

        #region [Support Methods]
        /// <summary>
        /// Uses reflection to get the field value from an object & type.
        /// </summary>
        /// <param name="type">The instance type.</param>
        /// <param name="instance">The instance object.</param>
        /// <param name="fieldName">The field's name which is to be fetched.</param>
        /// <returns>The field value from the object.</returns>
        internal static object GetInstanceField(Type type, object instance, string fieldName)
        {
            // IN-LINE USAGE: var str = GetInstanceField(typeof(Settings), this, "FontName") as string;
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field == null ? null : field.GetValue(instance);
        }
        internal static object GetInstanceField<T>(T instance, string fieldName)
        {
            // IN-LINE USAGE: var str = (string)GetInstanceField(instance, "FontName");
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            FieldInfo field = typeof(T).GetField(fieldName, bindFlags);
            return field == null ? null : field.GetValue(instance);
        }
        #endregion [Support Methods]
    }
}
