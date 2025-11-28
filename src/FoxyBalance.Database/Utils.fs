namespace FoxyBalance.Database

open System.Data
open System.Threading.Tasks

module internal Sql =
    /// Returns the only element in the result, or None if the result is empty or more than one element is returned.
    let tryExactlyOne (job : Task<'a list>) = task {
        let! result = job
        return Seq.tryExactlyOne result
    }
    
    /// Ignores the result of a task.
    let ignore (job : Task<_>) =
        task {
            let! _ = job
            ()
        } :> Task
    
    /// Maps the result of a job.
    let map (fn : 'a -> 'b) (job : Task<'a>) = task {
        let! result = job
        return fn result
    }
