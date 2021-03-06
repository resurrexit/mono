using System;

class EmitCtx
{
	int iarg, farg;

	public string Emit (char c) {
		switch (c) {
		case 'I':
			iarg += 1;
			return $"(int)margs->iargs [{iarg - 1}]";
		case 'F':
			farg += 1;
			return $"*(float*)&margs->fargs [FIDX ({farg - 1})]";
		case 'L':
			iarg += 2;
			return $"get_long_arg (margs, {iarg - 2})";
		case 'D':
			farg += 1;
			return $"margs->fargs [FIDX ({farg - 1})]";
		default:
			throw new Exception ("IDK how to handle " + c);
		}
	}
}

class Driver {
	static string[] cookies = new string[] {
		"V",
		"VI",
		"VII",
		"VIII",
		"VIIII",
		"VIIIII",
		"VIIIIII",
		"I",
		"II",
		"III",
		"IIII",
		"IIIII",
		"IIIIII",
		"IIIIIII",
		"IIIIIIIII",
		"L",
		"LL",
		"LI",
		"LIL",
		"LILII",
		"DD",
		"DDD",
		"VIF",
		"VIFF",
		"VIFFFF",
		"VIFFFFFI",
		"FF",
		"DI",
		"FI",
		"IIL",
		"IILI",
		"IILLLI",
		"LII",
		"VID",
		"VIIIIIII",
		"VILLI",

		"DID",
		"DIDD",
		"FIF",
		"FIFF",
		"LILL",
		"VIL",
	};
 
	static string TypeToSigType (char c) {
		switch (c) {
		case 'V': return "void";
		case 'I': return "int";
		case 'L': return "gint64";
		case 'F': return "float";
		case 'D': return "double";
		default:
			throw new Exception ("Can't handle " + c);
		}
	}

	static void Main () {		
		foreach (var c in cookies) {
			Console.WriteLine ("static void");
			Console.WriteLine ($"wasm_invoke_{c.ToLower ()} (void *target_func, InterpMethodArguments *margs)");
			Console.WriteLine ("{");

			
			Console.Write ($"\t{TypeToSigType (c [0])} (*func)(");
			for (int i = 1; i < c.Length; ++i) {
				char p = c [i];
				if (i > 1)
					Console.Write (", ");
				Console.Write ($"{TypeToSigType (p)} arg_{i - 1}");
			}
			if (c.Length == 1)
				Console.Write ("void");

			Console.WriteLine (") = target_func;\n");

			var ctx = new EmitCtx ();

			Console.Write ("\t");
			if (c [0] != 'V')
				Console.Write ($"{TypeToSigType (c [0])} res = ");

			Console.Write ("func (");
			for (int i = 1; i < c.Length; ++i) {
				char p = c [i];
				if (i > 1)
					Console.Write (", ");
				Console.Write (ctx.Emit (p));
			}
			Console.WriteLine (");");

			if (c [0] != 'V')
				Console.WriteLine ($"\t*({TypeToSigType (c [0])}*)margs->retval = res;");

			Console.WriteLine ("\n}\n");
		}

		Console.WriteLine ("static void\nicall_trampoline_dispatch (const char *cookie, void *target_func, InterpMethodArguments *margs)");
		Console.WriteLine ("{");
		for (int i = 0; i < cookies.Length; ++i) {
			var c = cookies [i];
			Console.Write ("\t");
			if (i > 0)
				Console.Write ("else ");
			Console.WriteLine ($"if (!strcmp (\"{c}\", cookie))");
			Console.WriteLine ($"\t\twasm_invoke_{c.ToLower ()} (target_func, margs);");
		}
		Console.WriteLine ("\telse {");
		Console.WriteLine ("\t\tprintf (\"CANNOT HANDLE COOKIE %s\\n\", cookie);");
		Console.WriteLine ("\t\tg_assert (0);");
		Console.WriteLine ("\t}");
		Console.WriteLine ("}");
	}
}
