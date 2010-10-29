//
// Copyright (C) 2010 Novell Inc. http://novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Markup;
using System.Xaml.Schema;

namespace System.Xaml
{
	static class TypeExtensionMethods
	{
		#region inheritance search and custom attribute provision

		public static T GetCustomAttribute<T> (this ICustomAttributeProvider type, bool inherit) where T : Attribute
		{
			foreach (var a in type.GetCustomAttributes (typeof (T), inherit))
				return (T) (object) a;
			return null;
		}

		public static T GetCustomAttribute<T> (this XamlType type) where T : Attribute
		{
			if (type.UnderlyingType == null)
				return null;

			T ret = type.GetCustomAttributeProvider ().GetCustomAttribute<T> (true);
			if (ret != null)
				return ret;
			if (type.BaseType != null)
				return type.BaseType.GetCustomAttribute<T> ();
			return null;
		}

		public static bool ImplementsAnyInterfacesOf (this Type type, params Type [] definitions)
		{
			return definitions.Any (t => ImplementsInterface (type, t));
		}

		public static bool ImplementsInterface (this Type type, Type definition)
		{
			if (type == null)
				throw new ArgumentNullException ("type");
			if (definition == null)
				throw new ArgumentNullException ("definition");
			if (type == definition)
				return true;

			foreach (var iface in type.GetInterfaces ())
				if (iface == definition || (iface.IsGenericType && iface.GetGenericTypeDefinition () == definition))
					return true;
			return false;
		}
		
		#endregion
		
		#region type conversion and member value retrieval
		
		public static string GetStringValue (this XamlType xt, object obj, INamespacePrefixLookup prefixLookup)
		{
			if (obj == null)
				return String.Empty;
			if (obj is Type)
				return new XamlTypeName (xt.SchemaContext.GetXamlType ((Type) obj)).ToString (prefixLookup);

			return (string) xt.ConvertObject (obj, typeof (string));
		}

		public static object ConvertObject (this XamlType xt, object target, Type explicitTargetType)
		{
			return DoConvert (xt.TypeConverter, target, explicitTargetType ?? xt.UnderlyingType);
		}
		
		public static object GetMemberValueForObjectReader (this XamlMember xm, XamlType xt, object target, INamespacePrefixLookup prefixLookup)
		{
			object native = GetPropertyOrFieldValueForObjectReader (xm, xt, target, prefixLookup);
			if (native == null)
				return null; // this is to prevent SringConverter to convert null to empty.
			var convertedType = xm.Type == null ? null : xm.Type.UnderlyingType;
			return DoConvert (xm.TypeConverter, native, convertedType);
		}
		
		static object DoConvert (XamlValueConverter<TypeConverter> converter, object value, Type targetType)
		{
			// First get member value, then convert it to appropriate target type.
			var tc = converter != null ? converter.ConverterInstance : null;
			if (tc != null && targetType != null && tc.CanConvertTo (targetType))
				return tc.ConvertTo (null, CultureInfo.InvariantCulture, value, targetType);
			return value;
		}

		static object GetPropertyOrFieldValueForObjectReader (this XamlMember xm, XamlType xt, object target, INamespacePrefixLookup prefixLookup)
		{
			// FIXME: should this be done here??
			if (xm == XamlLanguage.Initialization)
				return target;
			if (xm == XamlLanguage.PositionalParameters) {
				var argdefs = xt.GetConstructorArguments ().ToArray ();
				string [] args = new string [argdefs.Length];
				for (int i = 0; i < args.Length; i++) {
					var am = argdefs [i];
					args [i] = GetStringValue (am.Type, GetMemberValueForObjectReader (am, xt, target, prefixLookup), prefixLookup);
				}
				return String.Join (", ", args);
			}

			var mi = xm.UnderlyingMember;
			var fi = mi as FieldInfo;
			if (fi != null)
				return fi.GetValue (target);
			var pi = mi as PropertyInfo;
			if (pi != null)
				return pi.GetValue (target, null);

			throw new NotImplementedException (String.Format ("Cannot get value for {0}", xm));
		}
		
		#endregion

		public static bool IsContentValue (this XamlMember member)
		{
			if (member == XamlLanguage.Initialization)
				return true;
			if (member == XamlLanguage.PositionalParameters)
				return true;
			return IsContentValue (member.Type);
		}

		public static bool IsContentValue (this XamlType type)
		{
			var t = type.UnderlyingType;
			if (Type.GetTypeCode (t) != TypeCode.Object)
				return true;
			else if (t == typeof (Type) || t == typeof (TimeSpan) || t == typeof (Uri)) // special predefined types
				return true;
			return false;
		}

		static bool ExaminePositionalParametersApplicable (this XamlType type)
		{
			if (!type.IsMarkupExtension || type.UnderlyingType == null)
				return false;

			var args = type.GetConstructorArguments ();
			if (args == null)
				return false;

			Type [] argTypes = (from arg in args select arg.Type.UnderlyingType).ToArray ();
			if (argTypes.Any (at => at == null))
				return false;
			var ci = type.UnderlyingType.GetConstructor (argTypes);
			return ci != null;
		}
	
		public static IEnumerable<XamlMember> GetAllObjectReaderMembers (this XamlType type, object instance, object dictionaryKey)
		{
			// FIXME: find out why only TypeExtension and StaticExtension yield this directive. Seealso XamlObjectReaderTest.Read_CustomMarkupExtension*()
			if (type == XamlLanguage.Type ||
			    type == XamlLanguage.Static ||
			    ExaminePositionalParametersApplicable (type) && type.ConstructionRequiresArguments) {
				yield return XamlLanguage.PositionalParameters;
				yield break;
			}

			// Note that if the XamlType has the default constructor, we don't need "Arguments".
			var args = type.ConstructionRequiresArguments ? type.GetConstructorArguments () : null;
			if (args != null && args.Any ())
				yield return XamlLanguage.Arguments;

			if (dictionaryKey != null)
				yield return XamlLanguage.Key;

			if (type.IsContentValue ())
				yield return XamlLanguage.Initialization;

			if (type.IsDictionary) {
				yield return XamlLanguage.Items;
				yield break;
			}

			IEnumerable en = null;
			foreach (var m in type.GetAllMembers ()) {
				// do not read constructor arguments twice (they are written inside Arguments).
				if (args != null && args.Contains (m))
					continue;
				// do not return non-public members. Not sure why .NET filters out them though.
				if (!m.IsReadPublic)
					continue;
				// do not return readonly property which holds a collection with no item.
				if (!m.IsReadOnly || (en = (IEnumerable) m.Invoker.GetValue (instance)) != null && en.GetEnumerator ().MoveNext ())
						yield return m;
			}
			
			if (type.IsCollection)
				yield return XamlLanguage.Items;
		}

		public static bool ListEquals (this IList<XamlType> a1, IList<XamlType> a2)
		{
			if (a1 == null || a1.Count == 0)
				return a2 == null || a2.Count == 0;
			if (a2 == null || a2.Count == 0)
				return false;
			if (a1.Count != a2.Count)
				return false;
			for (int i = 0; i < a1.Count; i++)
				if (a1 [i] != a2 [i])
					return false;
			return true;
		}

		public static IEnumerable<XamlMember> GetConstructorArguments (this XamlType type)
		{
			return type.GetAllMembers ().Where (m => m.UnderlyingMember != null && m.GetCustomAttributeProvider ().GetCustomAttribute<ConstructorArgumentAttribute> (false) != null);
		}

		public static IEnumerable<XamlMember> GetSortedConstructorArguments (this XamlType type)
		{
			var args = type.GetConstructorArguments ().ToArray ();
			foreach (var ci in type.UnderlyingType.GetConstructors ().Where (c => c.GetParameters ().Length == args.Length)) {
				var pis = ci.GetParameters ();
				if (args.Length != pis.Length)
					continue;
				bool mismatch = false;
				foreach (var pi in pis)
				for (int i = 0; i < args.Length; i++)
					if (!args.Any (a => a.ConstructorArgumentName () == pi.Name))
						mismatch = true;
				if (mismatch)
					continue;
				return args.OrderBy (c => pis.FindParameterWithName (c.ConstructorArgumentName ()).Position);
			}
			return null;
		}

		static ParameterInfo FindParameterWithName (this IEnumerable<ParameterInfo> pis, string name)
		{
			return pis.FirstOrDefault (pi => pi.Name == name);
		}

		public static string ConstructorArgumentName (this XamlMember xm)
		{
			var caa = xm.GetCustomAttributeProvider ().GetCustomAttribute<ConstructorArgumentAttribute> (false);
			return caa.ArgumentName;
		}
	}
}
