namespace Pomo.Core

module AudioSystem =
  open Microsoft.Xna.Framework.Audio
  open Microsoft.Xna.Framework.Content

  let mutable fireBlip: SoundEffect = null

  let load(content: ContentManager) =
    fireBlip <- content.Load<SoundEffect>("Audio/FireBlip")

  let playFireBlip() =
    if not(isNull fireBlip) then
      fireBlip.Play() |> ignore
