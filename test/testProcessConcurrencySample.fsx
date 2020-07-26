#!/usr/bin/env fsx
System.Console.WriteLine("foo")
System.Console.Out.Flush()

System.Console.Error.WriteLine("bar")
System.Console.Error.Flush()

System.Console.WriteLine("baz")
System.Console.Out.Flush()
