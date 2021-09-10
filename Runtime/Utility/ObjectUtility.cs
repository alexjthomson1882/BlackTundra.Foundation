﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Linq;

namespace BlackTundra.Foundation.Utility {

    /// <summary>
    /// Implements utility methods for objects.
    /// </summary>
    public static class ObjectUtility {

        #region constant

        /// <summary>
        /// Dictionary used to cache the size of types.
        /// </summary>
        private static readonly Dictionary<Type, int> SizeOfDictionary;

        /// <summary>
        /// Contains overrides for non-serializable types to serializable implementations of that type.
        /// </summary>
        /// <seealso cref="SerializableImplementationOfAttribute"/>
        private static readonly Dictionary<Type, Type> TypeOverrideDictionary;

        /// <summary>
        /// An array containing a reference to every <see cref="Type"/>.
        /// </summary>
        private static readonly Type[] types;

        #endregion

        #region constructor

        static ObjectUtility() {
            types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToArray();
            if (types == null) throw new ApplicationException("No types found in assemblies in current domain.");
            SizeOfDictionary = new Dictionary<Type, int>();
            TypeOverrideDictionary = new Dictionary<Type, Type>();
            IEnumerable<Type> serializableImplementations = GetDecoratedTypes<SerializableImplementationOfAttribute>();
            foreach (Type implementation in serializableImplementations) {
                if (!implementation.HasAttribute<SerializableAttribute>()) {
                    Console.Error($"Invalid serializable implementation \"{implementation.FullName}\": Not decorated with {typeof(SerializableAttribute).FullName}.");
                    continue;
                }
                SerializableImplementationOfAttribute[] attributes = implementation.GetAttributes<SerializableImplementationOfAttribute>();
                SerializableImplementationOfAttribute attribute;
                for (int i = attributes.Length - 1; i >= 0; i--) {
                    attribute = attributes[i];
                    if (TypeOverrideDictionary.TryGetValue(attribute.target, out Type original)) { // already contains implementation
                        Console.Error($"Duplicate serializable implementation of type \"{attribute.target.FullName}\" (original: \"{original.FullName}\", current: \"{implementation.FullName}\").");
                        continue;
                    }
                    ConstructorInfo constructorInfo = implementation.GetConstructor( // get constructor information for target type
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { attribute.target },
                        null
                    );
                    if (constructorInfo == null) { // no constructor for target type
                        Console.Error($"Type \"{implementation.FullName}\" contains an invalid serializable implementation of type \"{attribute.target.FullName}\": No constructor with signature \"({attribute.target.FullName})\" found.");
                        continue;
                    }
                    TypeOverrideDictionary.Add(attribute.target, implementation); // register this implementation as a serializable implementation of the target type
                    Console.Info($"[ObjectUtility] Bound \"{attribute.target.FullName}\" -> \"{implementation.FullName}\"");
                }
            }
        }

        #endregion

        #region logic

        #region Size

        public static int SizeOf<T>() where T : struct => Marshal.SizeOf(default(T));

        public static int SizeOf(this Type type) {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (SizeOfDictionary.TryGetValue(type, out int size)) return size;
            size = SizeOfType(type);
            SizeOfDictionary.Add(type, size);
            return size;
        }

        #endregion

        #region SizeOfType

        private static int SizeOfType(in Type type) {
            DynamicMethod dynamicMethod = new DynamicMethod("SizeOfType", typeof(int), new Type[] { });
            ILGenerator il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Sizeof, type);
            il.Emit(OpCodes.Ret);
            return (int)dynamicMethod.Invoke(null, null);
        }

        #endregion

        #region SerializeToBytes

        /// <summary>
        /// Serializes an object to a byte array.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        /// <param name="includeType">
        /// When true, the object will be serialized with the object type. This is useful for deserializing an object you don't know
        /// the type of.
        /// </param>
        public static byte[] SerializeToBytes(this object obj, in bool includeType = false) {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            Type type = obj.GetType(); // get the type of the object
            bool serializable = type.IsSerializable; // check if the type can be serialized
            bool serializeFromMemory; // create a boolean to track if the object can be serialized directly from its value in memory
            byte flag = (byte)(serializable ? 0x01 : 0x00); // create a flag that helps the deserializer identify how the object has been serialized
            if (!serializable) { // the object is not by default serializable but could be translated into a serializable format
                serializeFromMemory = false; // mark as false so success can be tracked
                if (TypeOverrideDictionary.TryGetValue(type, out Type newType)) { // check if the type if overridden
                    ConstructorInfo constructor = newType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { type }, null); // find the constructor to construct the new type
                    if (constructor != null) { // the constructor was found
                        obj = constructor.Invoke(new object[] { obj }); // construct the new type
                        type = newType;
                        serializeFromMemory = true; // mark as true, the object can now be serialized from memory
                    } else {
                        Console.Error($"Failed to find constructor for object serialization type override {type.FullName} -> {newType.FullName}."); // log failure
                    }
                }
                if (!serializeFromMemory) throw new NotSupportedException($"Cannot serialize object of type \"{type.FullName}\".");
            } else { // the object is by default serializable
                serializeFromMemory = type.IsValueType && (type.IsPrimitive || type.IsEnum); // a type is serializable from memory if it is a value type and is primitive or an enum
            }
            if (serializeFromMemory && !includeType) { // can be taken directly from memory
                int size = Marshal.SizeOf(obj); // find the size in memory that the object will take up, all value-types should take the same amount of memory as eachother
                byte[] buffer = new byte[size + 1]; // create a buffer with enough space for the flag and serialized bytes
                IntPtr bufferPtr = Marshal.AllocHGlobal(size); // create a pointer to use in the buffer
                try {
                    Marshal.StructureToPtr(obj, bufferPtr, false); // convert the stucture in memory to a pointer (get a pointer to the object)
                    Marshal.Copy(bufferPtr, buffer, 1, size); // copy the contents of the memory structure to the buffer using the pointer
                } finally {
                    Marshal.FreeHGlobal(bufferPtr); // free the pointer
                }
                flag |= 0x02; // assign a flag that tells the deserializer that the object was serialized from memory
                buffer[0] = flag; // write the flag to the buffer
                return buffer; // return the serialized bytes that have now been written to the buffer
            } else { // requires binary formatter for serialization
                BinaryFormatter binaryFormatter = new BinaryFormatter(); // create a binary formatter to format the object (which is likely a class)
                using (MemoryStream memoryStream = new MemoryStream()) { // create a memory stream
                    try {
                        binaryFormatter.Serialize(memoryStream, obj); // try to serialize the object to the memory stream
                    } catch (SerializationException exception) {
                        Console.Error($"Failed to serialize object of type \"{type.FullName}\".", exception);
                        throw exception;
                    }
                    return memoryStream.ToArray().AddFirst(flag); // convert the contents of the memory stream to a byte array and make sure to add the flag to the start
                }
            }
        }

        #endregion

        #region ToObject

        public static T ToObject<T>(in byte[] bytes) {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            int size = bytes.Length - 1;
            if (size < 1) return default;

            byte flag = bytes[0]; // get the flag
            bool wasSerializable = (flag & 0x01) != 0x00; // the data was serializable in its original format
            bool wasSerializedFromMemory = (flag & 0x02) != 0x00; // the data was serialized directly from memory

            Type type = typeof(T);

            if (wasSerializable && TypeOverrideDictionary.TryGetValue(type, out Type newType)) // object was marked as serializable from memory
                type = newType; // convert to override type

            object obj;
            if (wasSerializedFromMemory) { // convert bytes directly to type in memory
                int typeSize;
                try {
                    typeSize = Marshal.SizeOf(type);
                } catch (ArgumentException exception) { // an ArgumentException is likely to be thrown if a type cannot be marshaled
                    throw new ArgumentException(
                        $"Failed to find size of \"{type.AssemblyQualifiedName}\".",
                        exception
                    );
                }
                if (size != typeSize) throw new ArgumentException($"Type size mismatch (type: \"{type.AssemblyQualifiedName}\", size: {size}, type size: {typeSize}).");

                IntPtr bufferPtr = Marshal.AllocHGlobal(size);
                try {
                    Marshal.Copy(bytes, 1, bufferPtr, size);
                    obj = Marshal.PtrToStructure(bufferPtr, type);
                } finally {
                    Marshal.FreeHGlobal(bufferPtr);
                }
            } else { // deserialize the data using a binary formatter
                using (MemoryStream memoryStream = new MemoryStream()) {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    memoryStream.Write(bytes, 1, bytes.Length - 1);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    obj = binaryFormatter.Deserialize(memoryStream);
                }
            }
            return (T)obj;
        }

        #endregion

        #region ToGUID

        public static int ToGUID(this string value) {
            if (value == null) return -1;
            unchecked {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;
                for (int i = 0; i < value.Length; i += 2) {
                    hash1 = ((hash1 << 5) + hash1) ^ value[i];
                    if (i == value.Length - 1) break;
                    hash2 = ((hash2 << 5) + hash2) ^ value[i + 1];
                }
                return hash1 + (hash2 * 1566083941);
            }
        }

        public static int ToGUID(this int value) => value;

        public static int ToGUID(this long value) {
            unchecked {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;
                hash1 = ((hash1 << 5) + hash1) ^ (int)(value & uint.MaxValue);
                hash2 = ((hash2 << 5) + hash2) ^ (int)((value >> 32) & uint.MaxValue);
                return hash1 + (hash2 * 1566083941);
            }
        }

        public static int ToGUID(this ulong value) {
            unchecked {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;
                hash1 = ((hash1 << 5) + hash1) ^ (int)(value & uint.MaxValue);
                hash2 = ((hash2 << 5) + hash2) ^ (int)((value >> 32) & uint.MaxValue);
                return hash1 + (hash2 * 1566083941);
            }
        }

        public static int ToGUID(this float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        public static int ToGUID(this byte[] buffer) {
            unchecked {
                const int p = 16777619;
                int hash = (int)2166136261;
                for (int i = 0; i < buffer.Length; i++)
                    hash = (hash ^ buffer[i]) * p;
                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }

        #endregion

        #region HasAttribute

        public static bool HasAttribute<T>(this Type type) where T : Attribute => type.GetCustomAttribute(typeof(T)) != null;

        #endregion

        #region GetAttribute

        public static T GetAttribute<T>(this Type type) where T : Attribute => type.GetCustomAttribute<T>();

        #endregion

        #region GetAttributes

        public static T[] GetAttributes<T>(this Type type) where T : Attribute => (T[])type.GetCustomAttributes<T>();

        #endregion

        #region GetDecoratedTypes

        /// <returns>
        /// Returns every <see cref="Type"/> that is decorated with an annotation of type
        /// <typeparamref name="T"/> as an <see cref="IEnumerable{T}"/>.
        /// </returns>
        public static IEnumerable<Type> GetDecoratedTypes<T>() where T : Attribute {
            Type context = typeof(T);
            Type type;
            for (int i = types.Length - 1; i >= 0; i--) {
                type = types[i];
                if (type.GetCustomAttributes(context, true).Length > 0)
                    yield return type;
            }
        }

        #endregion

        #region GetImplementations

        /// <returns>
        /// Returns all objects that inherit from <typeparamref name="T"/>.
        /// </returns>
        public static IEnumerable<Type> GetImplementations<T>() where T : class {
            Type context = typeof(T);
            Type type;
            for (int i = types.Length - 1; i >= 0; i--) {
                type = types[i];
                if (type.IsClass && type.IsSubclassOf(context))
                    yield return type;
            }
        }

        #endregion

        #region GetDecoratedMethods

        public static IEnumerable<MethodInfo> GetDecoratedMethods<T>(BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) where T : Attribute
            => types.Where(x => x.IsClass).SelectMany(x => x.GetMethods(bindingFlags)).Where(x => x.GetCustomAttributes(typeof(T), false).FirstOrDefault() != null); // find methods decorated with correct attribute

        #endregion

        #region GetValidDecoratedMethods

        /// <returns>
        /// Returns every method decorated with the <typeparamref name="T"/> attribute that follows the <see cref="MethodImplementsAttribute"/>
        /// method signature definitions. If a method is found decorated with the <typeparamref name="T"/> attribute that does not follow a
        /// pre-defined <see cref="MethodImplementsAttribute"/> method signature definition, a <see cref="NotSupportedException"/> will be
        /// thrown. A <see cref="NotSupportedException"/> will also be thrown if the <typeparamref name="T"/> attribute is not decorated with
        /// at least one <see cref="MethodImplementsAttribute"/>.
        /// </returns>
        public static IEnumerable<MethodInfo> GetValidDecoratedMethods<T>(BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) where T : Attribute {
            Type context = typeof(T); // get the context to check
            MethodImplementsAttribute[] implementations = context.GetCustomAttributes<MethodImplementsAttribute>(false).ToArray(); // get implementation definitions
            if (implementations.Length == 0) throw new NotSupportedException($"Attribute {nameof(T)} is not decorated with a {nameof(MethodImplementsAttribute)}."); // check for implementation definitions
            // temp variables:
            Type type;
            MethodInfo[] methods;
            MethodInfo method;
            ParameterInfo[] parameters;
            Type[] parameterTypes;
            MethodImplementsAttribute implementation;
            Type[] signature;
            bool valid = false;
            for (int i = types.Length - 1; i >= 0; i--) { // iterate each type that exists in the application
                type = types[i]; // get the current type
                if (type.IsClass) { // the type is a class
                    methods = type.GetMethods(bindingFlags); // get each method implemented in the method
                    for (int j = methods.Length - 1; j >= 0; j--) { // iterate each method in the class
                        method = methods[j]; // get the current method
                        if (method.GetCustomAttributes(context, false).FirstOrDefault() != null) { // method is decorated with target type
                            parameters = method.GetParameters(); // get the method parameters for the current method
                            int parameterCount = parameters.Length; // get the number of parameters in the current method
                            parameterTypes = new Type[parameterCount]; // construct an array of target types that must match a valid implementation
                            if (parameterCount > 0) { // there are parameters in the method signature
                                for (int k = parameterCount - 1; k >= 0; k--) { // iterate each parameter in the method signature
                                    parameterTypes[k] = parameters[k].ParameterType; // assign the signature types
                                }
                            }
                            for (int k = implementations.Length - 1; k >= 0; k--) { // iterate each possible implementation the method may follow
                                implementation = implementations[k]; // get the current implementation
                                signature = implementation.signature; // get the signature required by the current implementation
                                if (signature.ContentEquals(parameterTypes)) { // invalid parameter length
                                    valid = true; // this is a valid implementation
                                    yield return method; // return the valid method
                                    break; // stop here
                                }
                            }
                            if (!valid) throw new NotSupportedException($"Method {method.DeclaringType.FullName}.{method.Name} does not comply with any {nameof(T)} implementations.");
                            valid = false; // reset the valid flag
                        }
                    }
                }
            }
        }

        public static IEnumerable<MethodInfo> GetValidDecoratedMethods<T1, T2>(T2 obj, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic) where T1 : Attribute where T2 : class {
            Type context = typeof(T1); // get the context to check
            MethodImplementsAttribute[] implementations = context.GetCustomAttributes<MethodImplementsAttribute>(false).ToArray(); // get implementation definitions
            if (implementations.Length == 0) throw new NotSupportedException($"Attribute {nameof(T1)} is not decorated with a {nameof(MethodImplementsAttribute)}."); // check for implementation definitions
            // temp variables:
            MethodInfo method;
            ParameterInfo[] parameters;
            Type[] parameterTypes;
            MethodImplementsAttribute implementation;
            Type[] signature;
            bool valid = false;
            MethodInfo[] methods = obj.GetType().GetMethods(bindingFlags); // get each method implemented in the method
            for (int i = methods.Length - 1; i >= 0; i--) { // iterate each method in the class
                method = methods[i]; // get the current method
                if (method.GetCustomAttributes(context, false).FirstOrDefault() != null) { // method is decorated with target type
                    parameters = method.GetParameters(); // get the method parameters for the current method
                    int parameterCount = parameters.Length; // get the number of parameters in the current method
                    parameterTypes = new Type[parameterCount]; // construct an array of target types that must match a valid implementation
                    if (parameterCount > 0) { // there are parameters in the method signature
                        for (int j = parameterCount - 1; j >= 0; j--) { // iterate each parameter in the method signature
                            parameterTypes[j] = parameters[j].ParameterType; // assign the signature types
                        }
                    }
                    for (int j = implementations.Length - 1; j >= 0; j--) { // iterate each possible implementation the method may follow
                        implementation = implementations[j]; // get the current implementation
                        signature = implementation.signature; // get the signature required by the current implementation
                        if (signature.ContentEquals(parameterTypes)) { // invalid parameter length
                            valid = true; // this is a valid implementation
                            yield return method; // return the valid method
                            break; // stop here
                        }
                    }
                    if (!valid) throw new NotSupportedException($"Method {nameof(T2)}.{method.Name} does not comply with any {nameof(T1)} implementations.");
                    valid = false; // reset the valid flag
                }
            }
        }

        #endregion

        #region GetMethodParameterTypes

        /// <returns>
        /// Returns a <see cref="Type"/> array matching the expected types of the <paramref name="methodInfo"/> provided.
        /// </returns>
        public static Type[] GetMethodParameterTypes(in MethodInfo methodInfo) {
            ParameterInfo[] parameterInfo = methodInfo.GetParameters();
            int parameterCount = parameterInfo.Length;
            Type[] types = new Type[parameterCount];
            if (parameterCount > 0) {
                for (int i = parameterCount - 1; i >= 0; i--) {
                    types[i] = parameterInfo[i].ParameterType;
                }
            }
            return types;
        }

        #endregion

        #region InvokeMethods

        public static void InvokeMethods<T>(in object[] args = null) where T : Attribute {
            IEnumerable<MethodInfo> methods = GetDecoratedMethods<T>();
            foreach (MethodInfo method in methods) {
                try {
                    method.Invoke(null, args);
                } catch (Exception exception) {
                    Console.Error($"Failed to invoked method \"{method.Name}\".", exception);
                }
            }
        }

        #endregion

        #region GetDelegateInfo

        /// <returns>
        /// Returns <see cref="MethodInfo"/> for a delegate of type <typeparamref name="T"/>.
        /// </returns>
        public static MethodInfo GetDelegateInfo<T>() where T : Delegate {
            MemberInfo[] memberInfo = typeof(T).GetMember("Invoke");
            if (memberInfo.Length == 0) throw new InvalidOperationException();
            return (MethodInfo)memberInfo[0];
        }

        #endregion

        #region GetDelegateParameterTypes

        /// <returns>
        /// Returns a <see cref="Type"/> array matching the expected types of the delegate of type <typeparamref name="T"/> provided.
        /// </returns>
        public static Type[] GetDelegateParameterTypes<T>() where T : Delegate => GetMethodParameterTypes(GetDelegateInfo<T>());

        #endregion

        #endregion

    }

}