// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.

open Commands
open System
open System.Net.Http

// Compelling command example:

let apiBase = "http://api.postcodes.io/postcodes/%s/%s"

let noFallback = (fun _ -> async { return None } )

let echoCommand<'t> = 
    mkCommand (fun cmd (i:'t) -> cmd {
        failwith "oogabooga"
        return i
    }) noFallback

let validatePostCode = 
    mkCommand (fun cmd pc -> cmd {
        let! valid = time_out 1000 <| get_stat<bool> (Uri(sprintf "http://api.postcodes.io/postcodes/%s/validate" pc))
        //let! valid2 = time_out 1000 <| get_stat<bool> (Uri(sprintf "http://api.postcodes.io/postcodes/%s/validate" pc))
        let! xy = echoCommand 5
        return valid
    }) noFallback

//let getFirstAddressFromRandomPostCode = 
//    mkCommand (fun () -> cmd {
//        let! randomPostCode = get_stat<PostcodeResult seq> (Uri "http://api.postcodes.io/postcodes/ha98hq/nearest")
//        let lst = List.ofSeq <| randomPostCode
//        let first = lst |> List.head
//        let pc = first.Postcode
//        // todo: this is basically just an async at this point
//        // it'll work; and if it fails with no fallback it'll throw
//        // which will short the remainder of this command; but if it returns a response failure for example
//        // that won't propagate; it'll just be an error

//        // work out how to make it propagate; eather don't have the commands be executable within themselves
//        // (e.g require a runCommand func or something); or have the no-fallback error
//        // throw exceptions of own types; which can be matched and turned into Failure's
//        let! isValid = validatePostCode pc
//        //for each pc
//        return ""
//        //return false
//    }) noFallback

[<EntryPoint>]
let main argv = 
    
    let client = new HttpClient()
    let res = runCommand client <| validatePostCode "blargh" |> Async.RunSynchronously
    //let res = validatePostCode "blargh" { Client = new HttpClient() } |> Async.RunSynchronously
    //res |> Console.WriteLine
    //getFirstAddressFromRandomPostCode() |> Async.RunSynchronously |> Console.WriteLine
    Console.ReadLine() |> ignore
    0 // return an integer exit code
