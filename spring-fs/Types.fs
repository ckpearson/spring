[<AutoOpen>]
module Types

    type PositiveInt = 
        private PositiveInt of int
            static member op_Explicit (PositiveInt pi) = pi

    let mkPositiveInt = function
        | i when i < 0 -> None
        | i -> PositiveInt i |> Some

    let positiveIntVal (PositiveInt pi) = pi