# VisualHardpointLimits
BattleTech mod (using BTML) that limits your loadout possibilities to what mechs have as a hardpoint. Only if a mech can visually have an AC20 as a hardpoint will you be able to add the AC20 to the loadout.

## Requirements
** Warning: Uses the experimental BTML mod loader that might change, come here again to check for updates **

* install [BattleTechModLoader v0.1-alpha](https://github.com/Mpstark/BattleTechModLoader/releases) using the [instructions here](https://github.com/Mpstark/BattleTechModLoader)

## Features
- Restrict weapon loadouts to what is supported by the mech model. BattleTech has some of the best looking models thx to MWO, however we can rarely see them fully used.
- Fix bad visual loadout selection issues, sometimes the wrong or ugly looking loadout was shown, a new algorithm should improve upon this.
- Added a quickdraw with a second missle slot on the left torso. Both missle slots don't allow for more than 15 missle tubes total, looks cooler now.

Here is an example for the visual limitation portion of the mod, take the Highlander assault mech:
The left torso has 2 missle hardpoint slots, however only one can mount an LRM20, the other is limited to LRM10. Without this mod you can mount an LRM20 also for the second slot, but it visually would only be showing up as LRM10. With this mod you can't mount the second LRM20 anymore, you have to take either an LRM10 or LRM5. Of course SRMs are still an option.
The left arm is also limited to an LRM15 and you can't mount an LRM20 at all.

There are also currently 3 configuration settings available:

Setting | Type | Default | Description
--- | --- | --- | ---
allowLRMsInSmallerSlotsForAll | boolean | default false | set this to true so all mechs can field an LRM20 even if missing the required hardpoints.
allowLRMsInSmallerSlotsForMechs | string[] | default ["atlas"] | a list of mechs that can field larger LRM sizes even in smaller slots. Allows cheating the same as battletech lore does.
allowLRMsInLargerSlotsForAll | boolean | default true | allow smaller sized LRMs to be used in larger sized hardpoint slots. E.g. an LRM10 should fit into an LRM20 slot.

## Additional Features

- Fixed movement stats bar. Movement now actually shows movement without jump jets, as jump jets movement can be deduced from number of used jump jet slots
- Fixed heat efficency stats bar. Heat efficiency is calculated based on how much heat capacity is unused after an alpha strike (+ some JJ heat logic from the original code). Should now also properly work with heat sinks, heat banks and heat exchangers.

## Download

Downloads can be found on [github](https://github.com/CptMoore/VisualHardpointLimits/releases).

## Install
After installing BTML, put into \BATTLETECH\Mods\ folder and launch the game.
