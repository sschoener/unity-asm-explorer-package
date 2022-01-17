using System;
using System.Linq;
using System.Reflection;

namespace AsmExplorer
{
    public static class Serialization
    {
        const BindingFlags k_AllBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        const string k_MethodEncodingGenericArgSeparator = ";;;with;;;";

        public static string EncodeMethod(MethodBase method)
        {
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
            {
                var info = method as MethodInfo;
                var genericDefinition = info.GetGenericMethodDefinition();
                string methodId = genericDefinition.ToString();
                var arguments = method.GetGenericArguments();
                methodId += k_MethodEncodingGenericArgSeparator + string.Join(";", arguments.Select(EncodeType));
                return methodId;
            }
            else
            {
                return method.ToString();
            }
        }

        public static MethodBase DecodeMethod(Type type, string encodedMethod)
        {
            int separator = encodedMethod.IndexOf(k_MethodEncodingGenericArgSeparator);
            Type[] genericArguments;
            string lookUpKey;
            if (separator < 0)
            {
                lookUpKey = encodedMethod;
                genericArguments = null;
            }
            else
            {
                lookUpKey = encodedMethod.Substring(0, separator);
                var arguments = encodedMethod.Substring(separator + k_MethodEncodingGenericArgSeparator.Length).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                genericArguments = arguments.Select(DecodeType).ToArray();
            }

            var methods = type.GetMethods(k_AllBindings);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].ToString() == lookUpKey)
                {
                    if (genericArguments != null)
                        return methods[i].MakeGenericMethod(genericArguments);
                    return methods[i];
                }
            }

            return null;
        }

        public static string EncodeCtor(ConstructorInfo ctor) => ctor.ToString();

        public static ConstructorInfo DecodeCtor(Type type, string ctorName)
        {
            var ctors = type.GetConstructors(k_AllBindings);
            for (int i = 0; i < ctors.Length; i++)
            {
                if (ctors[i].ToString() == ctorName)
                    return ctors[i];
            }

            return null;
        }

        public static string EncodeType(Type type) => type.AssemblyQualifiedName;
        public static Type DecodeType(string encodedType) => Type.GetType(encodedType, false, false);
    }
}
