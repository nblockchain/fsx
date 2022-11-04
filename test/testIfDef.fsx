#!/usr/bin/env fsx

#if SOME_CONSTANT
printf "pre-hello1"
#else
printf "pre-hello2"
#endif

System.Console.WriteLine("hello world")
