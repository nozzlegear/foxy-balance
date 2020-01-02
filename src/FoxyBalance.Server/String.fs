module String
    let isEmpty = System.String.IsNullOrWhiteSpace
    
    /// Active pattern to check if a string is null, empty or whitespace.
    let (|EmptyOrWhitespace|NotEmpty|) x =
        match isEmpty x with
        | true ->
            EmptyOrWhitespace
        | false ->
            NotEmpty x
    
    /// Returns the default value if the actual value is null, empty or whitespace.
    let defaultValue defaultValue actualValue =
        match actualValue with
        | EmptyOrWhitespace ->
            defaultValue
        | NotEmpty x ->
            x 
