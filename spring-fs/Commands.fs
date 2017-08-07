module Commands
    open System
    open System.Net.Http
    open Newtonsoft.Json

    // Indicates the failure type of a command
    type Failure =
        | Exception of Exception
        | Pool
        | Breaker
        | Timeout
        | Response
    
    // A command asynchronously returns either a value or a failure
    type CommandResult<'t> = Async<Choice<'t,Failure>>

    // A command is any func that takes an arg and returns a command result
    type Command<'a,'r> = 'a -> CommandResult<'r>

    // A network fetch is any func that takes an httpclient and returns
    // a command result (may want to differentiate as not likely to suffer from pool errors)
    type NetFetch<'t> = HttpClient -> CommandResult<'t>

    // A net command is like a command; but it does a network request
    type NetCommand<'a,'r> = 'a -> NetFetch<'r>


    let ntwk<'t>
        (fetch:(HttpClient->Async<HttpResponseMessage>)) 
        (parse:(HttpResponseMessage->Async<'t>)) : NetFetch<'t> =
            fun client -> async {
                let! fr = Async.Catch <| fetch client
                return! match fr with
                        | Choice2Of2 xn -> async { return Choice2Of2 <| Exception xn }
                        | Choice1Of2 v -> async {
                            let! pr = Async.Catch <| parse v
                            return match pr with
                                    | Choice2Of2 xn -> Choice2Of2 <| Exception xn
                                    | Choice1Of2 v -> Choice1Of2 v
                        }
        }

    
    
    let private get_addr (addr:Uri) (client:HttpClient) = 
        client.GetAsync(addr) |> Async.AwaitTask

    let private post_addr (addr:Uri) (v) (client:HttpClient) =
        client.PostAsJsonAsync(addr,v) |> Async.AwaitTask

    let private read_json<'t> (resp:HttpResponseMessage) =
        resp.Content.ReadAsStringAsync()
        |> Async.AwaitTask
        |> (fun sa -> async {
            let! r = sa
            let deser = r |> JsonConvert.DeserializeObject<'t>
            return deser
        })

    // JSON-First APIs tend to always return HTTP/200; and instead
    // rely on a status field
    type JsonStatusObject<'t> =
        {
            Status : int;
            Result: 't
        }

    // Much like performing a net fetch;
    // this does the same, but also handles the inner field
    // coming back non-successful
    let ntwk_stat<'t> fetch =
        fun client -> async {
            let! res = ntwk fetch (read_json<JsonStatusObject<'t>>) client
            return match res with
                    | Choice2Of2 f -> f |> Choice2Of2
                    | Choice1Of2 jso ->
                        match jso with
                        | { Status = 200 } -> jso.Result |> Choice1Of2
                        | _ -> Choice2Of2 Response
        }

    let get<'t> (addr:Uri) : NetFetch<'t> =
        ntwk (get_addr addr) (read_json<'t>)

    let get_stat<'t> (addr:Uri) : NetFetch<'t> =
        ntwk_stat (get_addr addr)

    let post_with_response<'t,'r> (addr:Uri) (v:'t) : NetFetch<'r> =
        ntwk (post_addr addr v) read_json<'r>

    let post<'t> (addr:Uri) (v:'t) : NetFetch<unit> =
        ntwk (post_addr addr v) (fun _ -> async { return () })

    // Helper func that takes a net fetch func and a timeout val
    // and will return timeout failure if timeout first
    let time_out<'t> (ms:int) (nf:NetFetch<'t>) : NetFetch<'t> =
        (fun (c:HttpClient) -> async {
            let timer = async {
                do! Async.Sleep ms
                return Timeout |> Choice2Of2 |> Some
            }
            
            let runner = async { 
                let! res = nf c
                return res |> Some
            }

            let! res = Async.Choice[timer; runner]
            return match res with
                    | None -> Exception (InvalidOperationException("Unknown")) |> Choice2Of2
                    | Some c -> c
        })

    // Monadic type used for representing the deferred command
    type CmdDef<'t> = unit -> CommandResult<'t>

    let private ZeroCmdDef<'t> : unit -> Async<Choice<'t,Failure>> = (fun () -> async { return Unchecked.defaultof<'t> |> Choice1Of2 })
    let private ValCmdDef<'t> (v:'t) : unit -> Async<Choice<'t,Failure>> = (fun () -> async { return v |> Choice1Of2 })
    let private FailCmdDef<'t> (f:Failure) : unit -> Async<Choice<'t,Failure>> = (fun () -> async { return f |> Choice2Of2 })
    
    // Computation expression builder
    type CommandBuilder<'a,'r>(client:HttpClient) =
        member this.Zero() = ZeroCmdDef<'r>
        member this.Return (x:'r) = ValCmdDef<'r> x

        // Bind the processing of a net fetch op
        member this.Bind<'t>(v:NetFetch<'t>,f) = 
            match (v client) |> Async.RunSynchronously with
            | Choice1Of2 v' -> f v'
            | Choice2Of2 f' -> FailCmdDef<'r> f'
            
        // Bind the processing of an arbitrary async op
        member this.Bind<'t>(v:Async<'t>,f) =
            match v |> Async.Catch |> Async.RunSynchronously with
            | Choice1Of2 v' -> f v'
            | Choice2Of2 xn -> FailCmdDef<'r> <| Exception xn
            

    let mkCommand<'a,'r> (f: 'a -> CmdDef<'r>) (fallback:Failure->Async<'r option>) = 
        fun (a:'a) -> async {
            let! r = (f a)()
            return! match r with
                    | Choice1Of2 r' -> async { return r'}
                    | Choice2Of2 f' -> async {
                        let! fallbackRes = fallback f'
                        return match fallbackRes with
                                | Some r' -> r'
                                | None -> failwith "failed"
                    }
        }
        
    type PostcodeResult = 
        {
            Postcode: string
        }
            

    let cmd<'a,'r>  = CommandBuilder<'a,'r>(new HttpClient())
