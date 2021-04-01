# Description
Allows you to place a splitter on top of conveyor belts, this will automatically connect everything.

# Conditions
Placing splitter this way requires additional belts (as it would with placing vanilla way).
The type of a belt is picked based on a belt that is getting connected to splitter.
A splitter takes up a 3x3 grid which is checked for belts.

#### Default splitter

|        | A grid |        |
|:------:|:------:|:------:|
|    x   |    g   |    x   |
|    g   |    g   |    g   |
|    x   |    g   |    x   |

#### Vertical 2-way splitter

|        | B grid |        |
|:------:|:------:|:------:|
|    x   |    x   |    x   |
|   a/g  |   a/g  |   a/g  |
|    x   |    x   |    x   |

#### Vertical 4-way splitter

|        | C grid |        |
|:------:|:------:|:------:|
|    x   |    a   |    x   |
|    g   |   a/g  |    g   |
|    x   |    a   |    x   |
 
x - must be empty (can be occupied if you have `SmolSplitters`)
a - optional belts in the air.
g - optional belt on the ground.
a/g - can have both

# Changelog
**2.0.3**

* Updated for `0.6.17.6112` game version

**2.0.2**

* Fixed an issue when some automatically placed belts couldn't be destroyed/updated in chain with other belts (Doesn't affect already placed belts).

**2.0.1**

* Updated for `0.6.17.5932` game version

**2.0.0**

* Reworked belt detection. No more need in central belt to connect everything.
* Added support for vertical splitters.
* Changed collsision detection. Now everything should work fine `SmolSplitters`.
* Added belt consumption for making connections (as it should have been from the start)

**1.0.1**

* Added mod dll =\

**1.0.0**

* Mod release.