open System
open System.IO

let version = 
        File.ReadLines "version.txt" 
        |> Seq.head

let splitted_version: array<int> = 
        version.Split [|'.'|] 
        |> Array.map System.Int32.Parse 

let major = splitted_version.[0]
let minor = splitted_version.[1]
let patch = splitted_version.[2]

let new_version: string = $"{major}.{minor}.{patch+1}"
printfn "New version: %A" new_version
File.WriteAllText("version.txt", new_version)

