module Task
    /// Maps the result of a task.
    let inline Map fn (computation : System.Threading.Tasks.Task<_>) =
        task {
            let! result = computation
            return fn result 
        }
        
    /// Maps the result of one task to another
    let inline Bind (fn : _ -> System.Threading.Tasks.Task<_>) (computation : System.Threading.Tasks.Task<_>) =
        task {
            let! result = computation
            return! fn result 
        }
    
    /// Ignores the result of a task. 
    let inline Ignore (computation : System.Threading.Tasks.Task<_>) =
        task {
            let! result = computation
            ignore result 
        } :> System.Threading.Tasks.Task 