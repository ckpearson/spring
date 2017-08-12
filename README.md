# Spring

[Hystrix](https://github.com/Netflix/Hystrix) is a lovely project for dealing with all sorts of resiliency and availability concerns, baked in as part of your code, and it makes the notion of interacting with your services explicit via commands.

The issues I face with using something like this are:

1. It's in Java (as a .NET dev, this is a problem)
2. It's **very** OO
  * It requires implementing commands as derived types
  * There's just **way too much** boilerplate
  * Invoking the commands requires external knowledge that that's what they are
    * Calling code has to new up an instance, and call an exec function to kick it off
    
I want the following properties of my version:
  1. It's completely opaque, commands just look like regular functions
  2. It's entirely asynchronous, if you want to wait on a result, do it yourself
  3. It's **bindable**
      * Commands should be able to invoke other commands without any overhead of passing through the `HttpClient` or what have you
      * Chaining commands should come with all the same resiliency guarantees
  4. It's declarative
      * Building commands shouldn't have to be complicated
      * Commands shouldn't have to concern themselves with receiving and passing on a `HttpClient`
      * Commands should get all their timeout, circuit-breaker and fallback behaviour **automatically** (as closely as possible)
      
This is still a huge work in progress, but let's take an example with what is (more or less) possible already:

```fsharp
type Customer = { Name : string; Email : string; SentWelcomeEmail: bool }
type EmailRequest = { Address : string; Subject : string; Body : string}

// Command that takes no args, returns a list of customers, or an empty list on failure
let getCustomers = mkCommand((fun () cmd -> cmd {
    let! allCustomers = get<Customer list>(Uri("http://myapi.com/api/customer"))
    return allCustomers
  }) (fun failureType -> async { return Some [] })
  
let sendEmail = mkCommand((fun (request:EmailRequest) cmd -> cmd {
    do! post<EmailRequest> (Uri("http://myapi.com/email")) request
  }) (fun failureType -> async { return None })
  
let sendWelcomeEmailsToCustomersThatNeedThem =
  mkCommand((fun () cmd -> cmd {
    let! allMyCustomers = getCustomers()
    let emailRequests =
      allMyCustomers
      |> List.filter (fun c -> not c.SentWelcomeEmail)
      |> List.map (fun c -> { Address = c.Email; Subject = "Welcome!"; Body = "Hi there; welcome to the app!"})
    for req in emailRequests do
      do! sendEmail req
  }) (fun failureType -> async { return None })
```

In this short bit of code, we've defined three commands, all with built in resiliency (though certain features like circuit-breaker etc aren't in there yet).

If calling `sendWelcomeEmailsToCustomersThatNeedThem` fails anywhere along the command chain in the following ways:
  1. Any command throws an exception
  2. Any command executes a network request that returns a non-success status code
  3. Any command times out (half-implemented)
Then the failure causes execution to short at that point, and invoke the fallback mechanisms (if any).

If no fallback is possible, the command throws a regular exception as you'd expect.

The nicety of this, is that it's all hidden away, everything like circuit-breaker, retry etc can be implemented hidden away with minimal up-front config.

Additionally, all the commands are surfaced with the following signatures:

1. `getCustomers` = `HttpClient -> unit -> async<Customer list>`
2. `sendEmail` = `HttpClient -> EmailRequest -> async<unit>`
3. `sendWelcom...` = `HttpClient unit -> -> async<unit>`

But with the magic of partial application, you can simply redefine them all as such:

```fsharp

let myClient = new HttpClient()

let invokableGetCustomers = getCustomers myClient
let invokableSendEmail = sendEmail myClient
// You get the picture...
```
And now calling a command is a simple as treating it like a regular function:

```fsharp
let myCustomers = getCustomers() |> Async.runSynchronously
```

All of the abstraction around failure, fallback, cache retrieval, circuit breaker is all hidden away.
