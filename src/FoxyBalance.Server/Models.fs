namespace FoxyBalance.Server.Models

type Session =
    { UserId : int }
    
module ViewModels = 
    type LoginViewModel =
        { Error : string option
          Username : string option }
     
module RequestModels =
    ()