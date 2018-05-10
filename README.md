# DynModLib
BattleTech mod (using BTML) that provides C# scripting abilities for ModTek mods.

## Features

- can compile whole mods when loading BattleTech, an example can be seen with [StatsFixMod](https://github.com/CptMoore/StatsFixMod)
- allows mod users to change code without installing Visual Studio or equivalent software
- allows modders to quickly change code without having to start Visual Studio

## Future Features

- provide modders the ability to selectively compile and use C# scripts

## Requirements and Installation
** Warning: Uses the experimental BTML and ModTek **

either
* install BattleTechModTools using [BattleTechModInstaller](https://github.com/CptMoore/BattleTechModTools/releases)

or
* install [BattleTechModLoader](https://github.com/Mpstark/BattleTechModLoader/releases) using [instructions here](https://github.com/Mpstark/BattleTechModLoader)
* install [ModTek](https://github.com/Mpstark/ModTek/releases) using [instructions here](https://github.com/Mpstark/ModTek)
* install DynModLib by putting it into the \BATTLETECH\Mods\ folder

## How to use

Before doing modding, make yourself familiar with [dnSpy](https://github.com/0xd4d/dnSpy/releases). You need this to know what to mod.

See an example project, e.g. [StatsFixMod](https://github.com/CptMoore/StatsFixMod).

Checklist of things to do:
* Make a copy of the folder in \BATTLETECH\Mods\ .
* Rename the folder to your mods name.
* Remove all *Patch.cs files from the example mod, you probably want to write your own.
* Open Control.cs and change the namespace used to your mods name.
* Open mod.json and change the DLL name to your mods name.
* Use [dnSpy](https://github.com/0xd4d/dnSpy/releases) to find out what code you want to overwrite.
* Add harmony patches, see the example project for how that is done.
* Go through all .cs files and make sure to use your mods name as the namespace.
* Adjust the readme.

## How to use Visual Studio

Once your project becomes more complicated you might want to switch to Visual Studio (VS) for developing mods.

Checklist of things to do:
* Install the latest Visual Studio Community Edition, it's free.
* Open the .sln files you also copied when copying the example project.
* Rename the solution and the project to your mods name.
* Go into project properties and change the default namespace and assembly name to your mods name.
* Fix the files VS can't find by removing them from the project.
* Add your new files to VS using "Add Exiting Item".

## How to publish a mod

Checklist of things to do:
* Make an account on GitHub.
* Create a repository with your mods name.
* Install git, use that to upload your mod sources to GitHub.
* Create a new release version on github
* Link to the sources download for the release, that should be enough for other people to install and run your mod.

## Development of DynModLib

Checkout the folder DnyModLib directly under \BATTLETECH\Mods\ and start developing.

## Downloads

Downloads can be found on [github](https://github.com/CptMoore/DynModLib/releases).
