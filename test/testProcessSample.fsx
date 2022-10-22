#!/usr/bin/env fsx
System.Console.WriteLine("foo")
System.Console.Out.Flush()

System.Console.Write("bar")
System.Console.Out.Flush()
System.Console.WriteLine System.String.Empty
System.Console.Out.Flush()

System.Console.WriteLine("baz")
System.Console.Out.Flush()
