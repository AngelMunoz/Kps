namespace Pomo.Core

open MonoGame.Framework.Utilities

module Platform =

  let IsMobile() =

    match PlatformInfo.MonoGamePlatform with
    | MonoGamePlatform.Android -> true
    | MonoGamePlatform.iOS -> true
    | _ -> false
