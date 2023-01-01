#!/usr/bin/env fsx

#if !LEGACY_FRAMEWORK
#r "test1.dll"
#else
#r "nonExistent.dll"
#endif

#if LEGACY_FRAMEWORK
#r "nonExistent.dll"
#else
#r "test1.dll"
#endif

NS.Inside.Foo()

System.Console.WriteLine("hello world")
