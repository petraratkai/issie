Tick 3
==========
Overall feedback on code
----------
XML codes used incorrectly in most places. Need to put "." at the end of the line in case of a multiline XML comment for punctuations
Also don't add XML comments within functions or records
```F#
// Thing represents a cicle or a rectangle on an SVG canvas
type Thing = { 
    /// unique ID
    Id: ThingId
    /// true if rectangle, false if circle
    IsRectangle: bool
    /// used only when dragging: 0,1,2,3 indicates side dragged
    Side: int // which side (of a rectangle) is currently being dragged 0: right, 1: bottom, 2: left, 3: top
    /// centre
    X: float // x coordinate of centre of Thing
    /// centre
    Y: float // y coordinate of centre of Thing
    /// width
    X1: float // width of rectangle or diameter (not radius) of circle
    /// height
    X2: float // height of rectangle
}
```
Bad field names X,Y,X1,X2 very confusing, different meaning for circle/rectangle and could use XYPos
isRectangle also not the best way to do it
change Thing to smth like this:
```F#
type Circle = {
    diameter: float
}
type Rectangle = {
    draggedSide: int
    width: float
    height: float
}
///represents circles or rectangles
type Shape = {
    | Circle of Circle
    | Rectangle of Rectangle
}
/// Thing represents a cicle or a rectangle on an SVG canvas
type Thing = { 
    // unique ID
    Id: ThingId
    // true if rectangle, false if circle
    Centre: XYPos

    Shape: Shape
}
```
Rectangle.draggedSide could be made a DU: 
```F#
type Side = Top | Right | Bottom | Left
```
Also could be an option:
```F#
type DraggedSide = Side Option
```


```F#
type Model3 = {
    /// how close mouse needs to be to an object to click on it
    ClickRadius: float
    /// map of all displayed Things keed by Id
    Things: Map<ThingId,Thing>
    /// true while something is being dragged
    Dragging: bool // is something being currently dragged to resize by the mouse
    /// Id of thing currently being dragged
    DraggedThing: ThingId // which Thing is being dragged
}
```
Dragging and DraggedThing could be changed to an option:
```F#
type Model3 = {
    /// how close mouse needs to be to an object to click on it
    ClickRadius: float
    /// map of all displayed Things keed by Id
    Things: Map<ThingId,Thing>
    /// Id of thing currently being dragged
    DraggedThing: ThingId Option // which Thing is being dragged
}
```

Section A
----------
```F#
/// returns true if the coordinate (X or Y) in common between the two side endpoints is positive
/// relative to the rectangle position
let sideHasPositiveCommonCoordinateOffset side =
    side = 0 || side = 1
```
Not sure what this function is meant to do. Very long name, function body is short though, so probably don't need this function
--> Remove this function, not needed

```F#
/// Return the two side endpoint sets of coordinates
/// for side s of rectangle center (c1,c2), width x1, height x2
/// The most positive end must be first
let getCoordinates s c1 c2 x1 x2 =
    match s with
    | 0 -> (c1 + x1/2.0, c2 + x2/2.0),(c1 + x1/2.0, c2 - x2/2.0)
    | 2 -> (c1 - x1/2.0, c2 + x2/2.0),(c1 - x1/2.0, c2 - x2/2.0)
    | 1 -> (c1 + x1/2.0, c2 + x2/2.0),(c1 - x1/2.0, c2 + x2/2.0)
    | 3 -> (c1 + x1/2.0, c2 - x2/2.0), (c1 - x1/2.0, c2 - x2/2.0)
    | _ -> (0. , 0.), (0. , 0.) // Return a default zero value for bad s to avoid exception
```
this function makes sense. Could it be made simpler? 
--> change return type to use XYPos so return a tuple of XYPos, get rid of the /2.0s to reduce noise
rename x1 x2 to w h /  use XYPos
could move s to the last parameter pos. because then can be curried
name should contain Rect, so for example getRectSideCoord

```F#
// get offset between side of rectangle and current mouse position
// direction = true => horizontal side
// (x1,y1): side end point (either will do)
// (x,y) current mouse pos
let subtractFromX1OrY1 direction x y x1 y1  =
    if direction then y - y1 else x - x1
```
too many parameters, bad variable names. x y and x1 y1 should be XYPos, call them mousePos and Side for example
Could naming be better? Maybe getXorYdist? Could do if direction \% 2 then ...
-->move this function into doSubtraction, use XYPos again?, bad variable names again for the parameters

```F#
// return movement needed when dragging to change the size of a rectangle thing
// as change in its X1, X2 components
// (x,y) is mouse position
// one of the component changes will be 0
// output is tuple in form X1,X2
// side = side that is being dragged by mouse
// thing = rectangle
let doSubtraction (thing: Thing) side x y =
    let cc1,cc2 = getCoordinates side thing.X thing.Y thing.X1 thing.X2
    let d = subtractFromX1OrY1 (side % 2 = 1) x y (fst cc1) (snd cc1) 
    let sign = if sideHasPositiveCommonCoordinateOffset side then 1. else -1.
    let offset = sign * d * 2.0
    match side % 2 with
    | 0 | 2 -> offset, 0.
    | 1 | 3 -> 0., offset
```
sideHasPositiveCommonCoordinateOffset way too long, better to have `if side < 2 then ...` for example, or different if side is a DU, that's probably the best
cc1, cc2 bad names call them start, end / corner1, corner2
make return type anonymous record: `{|width = offset; height = 0.|}`
rename to `getWidthHeightChange` maybe

```F#
/// Alter size of currently dragged thing to make its edge (or its clicked side) follow pos
/// For circles the circle should go through pos
/// For rectangles pos shoudl be colinear with the dragged side (common coordinate the same)
let dragThing (pos: XYPos) (model: Model3) =
    let tId = model.DraggedThing
    if not <| Map.containsKey tId model.Things then  
        failwith $"Unexpected ThingId '{tId}' found in model.DragThing by dragThing"
    let tMap = model.Things
    let thing = tMap.[tId]
    if thing.IsRectangle then 
        let side = thing.Side
        let x1,x2 = doSubtraction thing side pos.X pos.Y
        let thing' = {thing with X1 = thing.X1 + x1; X2 = thing.X2 + x2}
        {model with Things = Map.add tId thing' tMap}
    else
        let centre = {X=thing.X;Y=thing.Y}
        let r' = euclideanDistance centre pos
        let thing' = {thing with X1 = r' * 2.0}
        {model with Things = Map.add tId thing' tMap}
```
rename x1,x2 to wDiff, hDiff
do subtraction could just be made into getUpdatedDims and return the new width and height

Section B
---------
```F#
/// sample parameters for drawing circle
let circParas = {
    ///  Radius of the circle
    R = 10.0    
    /// color of outline: default => black color
    Stroke ="blue"
    /// width of outline: default => thin
    StrokeWidth ="2px"
    /// Fill: 0.0 => transparent, 1.0 => opaque
    FillOpacity= 0.0 // transparent fill
    /// color of fill: default => black color
    Fill = "" // default
}

/// sample parameters for drawing lines
let lineParas: Line = {
    /// color of outline: default => black color
    Stroke = "green"
    /// width of outline: default => thin
    StrokeWidth = "2px"
    /// what type of line: default => solid
    StrokeDashArray = "" //default solid line
}
```
These types are good, maybe call them circleParams and lineParams, also XML comments inside the types shouldn't be there

Section C
---------

```F#
/// is a rectangle side (determined by its two endpoints) clicked
let clickedSideOpt clickRadius (pos:XYPos) (i,((x1,y1),(x2,y2))) =
    if abs (x1 - x2) > abs (y1 - y2) then
        // it is a horizontal side
        if abs (pos.Y - y1) < clickRadius && x1 > pos.X && pos.X > x2 then
            Some i
        else
            None
    else 
        if abs (pos.X - x1) < clickRadius && y1 > pos.Y && pos.Y > y2 then
            Some i
        else
            None
```
Should call getCoordinates here instead of passing `(i,((x1,y1),(x2,y2)))` so pass Thing instead of this complicated structure
Also bad variable names, complicated structure, why is the side called i?
Should improve if getCoordinates was updated to use XYPos in the return type

```F#
/// return None or the thing (and possibly side, for rectangles) clicked
let clickedThingOpt (clickRadius: float) (pos:XYPos) (thingId: ThingId) (thing: Thing):
        {|ThingId: ThingId; ItemSide:int|} option =
    if thing.IsRectangle then
        [0..3]
        |> List.map (fun side -> side, getCoordinates side thing.X thing.Y thing.X1 thing.X2)
        |> List.tryPick (clickedSideOpt clickRadius pos)
        |> Option.map (fun side -> {|ThingId = thingId; ItemSide = side|})
    elif abs (euclideanDistance pos {X=thing.X;Y=thing.Y} - thing.X1 / 2.0) < 5 then
        let ed = euclideanDistance pos {X=thing.X;Y=thing.Y}
        let rad = thing.X1 / 2.0
        Some {|ThingId = thingId; ItemSide = 0|}
    else 
        None
```
thingId does not need to be passed into the function because think already contains it
List.map line is hard to understand, is not needed if getCoordinates is called in clickedSideOpt


            



