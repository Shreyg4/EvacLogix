Name: Evaclogix

UW community impact statement: Educate users on proper evacuation routes in given buildings as well as addressing the design safety of buildings.

Unity 2D: Map only the buildings, with specific exit spaces and points for the agents to run to.  Sprinklers will also be hand added. We are simulating the whole building, windows could have their own weights as exits (need to try normal exits \+ floors), stairways are teleportation zones to the next floors.

Our group will handdraw the building layouts with hitboxes and weights from existing building blueprints. Our goal is to create 2-3 building layouts by the end. We will possibly implement a custom build/paint tool that supports: Overlaying an existing image to allow for tracing, placing teleportation objects and obstacles by hand, drawing walls (line tool or brush, with sizing), window objects and escape objects (goals). 

Will use unity’s nav mesh system with built in A\* using polygon graphs for the individual agents. Congestion control will involve higher weights for crowded spaces with other agents. Backup plan in case of poor performance or implementation: Grid based weight graph.

Graph based system so agents can have defined movement speeds, wall collisions, obstacle handling, and other features. 

Agents will represent students as circles with collision to represent crowding behavior. We will support around 300 agents.

State machine for agents: Escaping, panicking, reflex response.

We will simulate: Fire

Variability in fire spread. 

Possibly post on unity play. An additional website will be designed for the final presentation.

Global timer “Building about to collapse”.

Blockades: Firespread, random events that cause other blockades (weight adjustment), 

Website tech stack: React, typescript, Unity Play/WebGL.

UI/UX for the Game/App. Different settings for sliders such as fire spread, agent speed multiplier, blocking settings. The user can choose where the fire starts. Users can add agents individually. 

Version control: Will first try github, if not, unity version control.

**Stretch Goals:**  
Possible heath bar per agent (Stretch goal)  
Agent/People demographics (Stretch)  
Evil professor (Stretch)  
Table obstacles (Stretch)

