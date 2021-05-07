//(*
//    FileMenuView.fs
//
//    View for the top menu, and related functionalities.
//*)

module FileMenuView

open Fulma
open Fable.React
open Fable.React.Props

open Helpers
open JSHelpers
open DiagramStyle
open ModelType
open CommonTypes
open FilesIO
open Extractor
open PopupView
open System
open Electron

//--------------------------------------------------------------------------------------------//
//--------------------------------------------------------------------------------------------//
//-------------------------------Custom Component Management----------------------------------//
//--------------------------------------------------------------------------------------------//


// Basic idea. When the I/Os of the current sheet are changed this may affect instances of the sheet embedded in other
// project sheets. Check for this whenever current sheet is saved (which will happen when Issie exits or the
// edited sheet is changed). Offer, in a dialog, to change all of the affected custom component instances to maintain
// compatibility. This will usually break the other sheets.
// all of this code uses the project data structure and (if needed) returns an updated structure.

type IODirection = InputIO | OutputIO

type IOMatchType = 
    | Identity // Ids are the same
    | BitsChanged of int // names and types are the same
    | NameChanged of string * int
    | Deleted  // the signature item no longer exists
    | AddedToInstances // the signature item must be added to instances

type Match = {
    MType: IOMatchType
    MLabel: string
    Width: int
    MDir: IODirection
    }

let getIOSignature (c: Component) =
    match c.Type with
    | Input n -> {MLabel = c.Label; Width = n ; MDir = InputIO; MType = Identity}
    | Output n -> {MLabel = c.Label ; Width = n ; MDir = OutputIO; MType = Identity}
    | _ -> failwithf "What? getIOSignature can only be used on input or output components"

let ioMatchComponents (c1:Component) (c2:Component) =
    match c1, c2 with
    | {Label = lab1; Type = Input n1}, {Label = lab2; Type = Input n2} 
    | {Label = lab1; Type = Output n1}, {Label = lab2; Type = Output n2} ->
        if lab1 = lab2 && n1 = n2 then
            Some (getIOSignature c1)
        elif lab1 = lab2 then
            Some {getIOSignature c1 with MType = BitsChanged (getIOSignature c2).Width}
        else None
    | {Id = id1}, {Id=id2} when id1 = id2 ->
            let c2Sig = getIOSignature c2
            Some { getIOSignature c1 with MType =  NameChanged( c2Sig.MLabel, c2Sig.Width)}
    | _ -> None

let ioMatchCompWithList (comps:Component list) (ioComp:Component)  =
    List.tryPick (ioMatchComponents ioComp)  comps
    |> Option.defaultValue {getIOSignature ioComp with MType = AddedToInstances}

let ioMatchLists (comps1: Component list) (comps2: Component list) =
    comps1
    |> List.map (ioMatchCompWithList comps2)

/// compare two I/O signature lists and return arrays:
/// Common: the matching I/Os (with how well they match).
/// Diffs: unmatched I/Os from either signature
/// IOMap: look up details of the match (matching comps1 IO with comps2)
let ioCompareLists (comps1: Component list) (comps2: Component list) =
    let map (ml: Match list) = 
        List.map (fun m -> (m.MDir, m.MLabel), m) ml |> Map.ofList
    let ioMap1 = ioMatchLists comps1 comps2 |> map
    let ioMap2 = ioMatchLists comps2 comps1 |> map
    let ioMap = mapUnion ioMap1 ioMap2
    let set1,set2 = set (mapKeys ioMap1), set (mapKeys ioMap2)
    let common = Set.intersect set1 set2
    let diff = (set1 - set2) + (set2 - set1)
    let common = 
        common
        |> Set.toArray
        |> Array.sortBy 
            (fun m -> 
                match ioMap.[m].MType with 
                | Identity -> 1 
                | BitsChanged _-> 2 
                | NameChanged _ ->  3 
                | _ -> 4)
    {| Common = common; Diffs= diff; IOMap = ioMap|}

let findInstancesOfCurrentSheet (project:Project) =
    let thisSheet = project.OpenFileName
    let ldcs = project.LoadedComponents
    let getInstance (comp:Component) =
        match comp.Type with
        | Custom ({Name=thisSheet} as cType) -> Some (ComponentId comp.Id, cType)
        | _ -> None

    let getSheetInstances (ldc:LoadedComponent) =
        fst ldc.CanvasState
        |> List.choose getInstance

    ldcs
    |> List.collect (fun ldc -> 
        getSheetInstances ldc
        |> List.map (fun ins -> ldc.Name, ins))

let updateDependents ins (p: Project) =
    ()

type Deps =
    | NoDependents
    | OneSig of ((string * int) list * (string * int) list) * (string * (ComponentId * CustomComponentType)) list
    | Mixed of (string * int) list

let getDependentsInfo (p: Project)  =
    let instances = findInstancesOfCurrentSheet p
    let gps = 
        instances
        |> List.groupBy (fun (_, (_,{InputLabels=ips; OutputLabels=ops})) -> (ips |> List.sort), (ops |> List.sort))
        |> List.sortByDescending (fun (tag,items) -> items.Length)

    match gps with
    | [] -> NoDependents // no dependencies - nothing to do
    | [sg, items] -> OneSig(sg, items) // normal case, all dependencies have same signature
    | _ -> // dependencies have mixed signatures
        instances
        |> List.groupBy fst
        |> List.map (fun (tag, lst) -> tag, lst.Length)
        |> Mixed

let changeAllDependents (p: Project) (dispatch: Msg -> Unit) =
    printfn "Changing dependents is not implemented yet!"

let displayDependentsInfoPopup (depL: (string * int) list) (model:Model) (dispatch: Msg -> Unit) = 
    match model.CurrentProj with
    | None -> ()
    | Some p ->

        let headCell heading = th [ ] [ str heading ]

        let row (sheetName, number) =
            tr [] 
                [ td [ ] [ str sheetName ]
                  td [ ] [ str $"%d{number}" ] ]
        
        let body = 
            div [] 
                [       
                    str "The current sheet is used as a custom component in multiple other sheets with different input or output ports. \
                        Issie can update all these instances automatically to be compatible with this sheet: you will need to check to see which require \
                        manual reconnection because of changed port definitions. Automatic update is normally the best option."
                    br []
                    str "Dependent instances"
                    br []
                    Table.table [ Table.IsHoverable ]
                        [ 
                            thead [ ] [ tr [] [headCell "Sheet"; headCell "Number"] ]                
                            tbody [ ] (List.map row depL)
                        ]
                ]
        let action doUpdate _  =
            if doUpdate then changeAllDependents p dispatch
        
        choicePopup 
            "Dependent Sheets use inconsistent definitions" 
            body  
            "Update all component instances" 
            "Save this sheet without updating dependents" 
            action
            dispatch

let makePortName name width =
    match width with
    | 1 -> name
    | w -> $"%s{name}({w-1}:{w}"
    |> str


let checkDependents (model: Model) (p: Project) (dispatch: Msg -> Unit) =
    match getDependentsInfo p with
    | NoDependents -> ()
    | Mixed lst ->
        displayDependentsInfoPopup lst model dispatch
    | OneSig ((inputSigs, outputSigs), instances) ->
        let headCell heading =  th [ ] [ str heading ]
        let makeRow isInput (name,width) = 
            tr []
                [
                    td [] [str (if isInput then "Input" else "Output")]
                    td [] [makePortName name width]
                    td [] [makePortName name width]
                    td [] [str "?"]
                ]
        let body = 
            div [] 
                [
                    str "You can automatically update all dependent sheets to match the current sheet."
                    str "Ports will need to be reconnected only if they cannot be automatically matched."
                    table []
                        [ 
                            thead [] [ tr [] (List.map headCell ["Type" ;"Old port"; "New port" ; "No change?"]) ]
                            tbody []   (List.map (makeRow true) inputSigs) 
                            tbody []   (List.map (makeRow false) outputSigs) 

                        ]
                ]

        let buttonAction isUpdate _ =
            if isUpdate then
                changeAllDependents p dispatch
        choicePopup 
            "Update Sheet Dependencies" 
            body 
            "Update all" 
            "Save without updating" 
            buttonAction 
            dispatch


       

 




//--------------------------------------------------------------------------------------------//
//--------------------------------------------------------------------------------------------//
//---------------------Code for CanvasState comparison and FILE BACKUP------------------------//
//--------------------------------------------------------------------------------------------//

/// Works out number of components and connections changed between two LoadedComponent circuits
/// a new ID => a change even if the circuit topology is identical. Layout differences do not
/// mean changes, as is implemented in the reduce functions which remove layout.
let quantifyChanges (ldc1:LoadedComponent) (ldc2:LoadedComponent) =
    let comps1,conns1 = ldc1.CanvasState
    let comps2,conns2 = ldc2.CanvasState
    let reduceComp comp1 =
        {comp1 with X=0;Y=0}
    let reduceConn conn1 =
        {conn1 with Vertices = []}
    /// Counts the number of unequal items in the two lists.
    /// Determine equality from whether reduce applied to each item is equal
    let unmatched reduce lst1 lst2 =
        let mapToSet = List.map reduce >> Set
        let rL1, rL2 = mapToSet lst1, mapToSet lst2
        Set.union (Set.difference rL1 rL2) (Set.difference rL2 rL1)
        |> Set.count
    unmatched reduceComp comps1 comps2, unmatched reduceConn conns1 conns2

////------------------------------------------Backup facility-------------------------------------------//

let writeComponentToFile comp =
    let data =  stateToJsonString (comp.CanvasState,comp.WaveInfo)
    writeFile comp.FilePath data

/// return an option containing sequence data and file name and directory of the latest
/// backup file for given component, if it exists.
let readLastBackup comp =
    let path = pathWithoutExtension comp.FilePath 
    let baseN = baseName path
    let backupDir = pathJoin [| dirName path ; "backup" |]
    latestBackupFileData backupDir baseN
    |> Option.map (fun (seq, fName) -> seq, fName, backupDir)
  
/// Write Loadedcomponent comp to a backup file if there has been any change.
/// Overwrite the existing backup file only if it is a small, and recent, change.
/// Parameters determine thresholds of smallness and recency
let writeComponentToBackupFile (numCircuitChanges: int) (numHours:float) comp = 
    let nSeq, backupFileName, backFilePath =
        match readLastBackup comp with
        | Some( n, fp, path) -> n+1,fp, path
        | None -> 0, "", pathJoin [|comp.FilePath; "backup"|]
    //printfn "seq=%d,name=%s,path=%s" nSeq backupFileName backFilePath
    let wantToWrite, oldFile =
        if backupFileName = "" then
            true, None
        else
            let oldBackupFile = pathJoin [|backFilePath ; backupFileName|]
            match tryLoadComponentFromPath (oldBackupFile) with
            | Ok comp' ->
                if not (compareIOs comp comp') then
                    true, None // need to save, to a new backup file
                elif compareCanvas 10000. comp.CanvasState comp'.CanvasState then
                    false, None // no need for a new backup
                else
                    let nComps,nConns = quantifyChanges comp' comp
                    let interval = comp.TimeStamp - comp'.TimeStamp
                    if interval.TotalHours > numHours || nComps + nConns  > numCircuitChanges then
                        true, None
                    else
                        true, Some oldBackupFile
                        
            | err -> 
                printfn "Error: writeComponentToBackup\n%A" err
                true, None
    if wantToWrite then
        let timestamp = System.DateTime.Now
        let backupPath =
                // work out new path to write based on time.
                let path = pathWithoutExtension comp.FilePath
                let baseN = baseName path
                let ds = EEExtensions.String.replaceChar '/' '-' (timestamp.ToShortDateString())
                let suffix = EEExtensions.String.replaceChar ' ' '-' (sprintf "%s-%02dh-%02dm" ds timestamp.Hour timestamp.Minute)
                let backupDir = pathJoin [| dirName path ; "backup" |]
                ensureDirectory <| pathJoin [| dirName path ; "backup" |]
                pathJoin [| dirName path ; "backup" ; sprintf "%s-%03d-%s.dgm" baseN nSeq suffix |]
        // write the new backup file
        {comp with 
            TimeStamp = timestamp
            FilePath = backupPath}
        |> writeComponentToFile
        /// if necessary delete the old backup file
        match oldFile with
        | Some oldPath when oldPath <> backupPath ->
            if Node.Api.fs.existsSync (Fable.Core.U2.Case1 oldPath) then
                Node.Api.fs.unlink (Fable.Core.U2.Case1 oldPath, ignore) // Asynchronous.
            else
                ()
        | _ -> ()

/// returns a WaveSimModel option if a file is loaded, otherwise None
let currWaveSimModel (model: Model) =
    match getCurrFile model with
    | Some fileName when Map.containsKey fileName (fst model.WaveSim) -> Some (fst model.WaveSim).[fileName]
    | _ -> None

let private displayFileErrorNotification err dispatch =
    let note = errorFilesNotification err
    dispatch <| SetFilesNotification note

/// Send messages to change Diagram Canvas and specified sheet waveSim in model
let private loadStateIntoModel (compToSetup:LoadedComponent) waveSim ldComps model dispatch =
    // it seems still need this, however code has been deleted!
    //Sheet.checkForTopMenu () // A bit hacky, but need to call this once after everything has loaded to compensate mouse coordinates.
    
    let sheetDispatch sMsg = dispatch (Sheet sMsg)
    let JSdispatch mess = 
        mess
        |> JSDiagramMsg
        |> dispatch
    let name = compToSetup.Name
    //printfn "Loading..."
    dispatch <| SetHighlighted([], []) // Remove current highlights.
    
    model.Sheet.ClearCanvas sheetDispatch // Clear the canvas.
    
    // Finally load the new state in the canvas.
    dispatch <| SetIsLoading true
    //printfn "Check 1..."
    
    let components, connections = compToSetup.CanvasState
    model.Sheet.LoadComponents sheetDispatch components
    
    model.Sheet.LoadConnections sheetDispatch connections

    model.Sheet.FlushCommandStack sheetDispatch // Discard all undo/redo.
    // Run the a connection widths inference.
    //printfn "Check 4..."
    
    model.Sheet.DoBusWidthInference sheetDispatch
    // JSdispatch <| InferWidths()
    //printfn "Check 5..."
    // Set no unsaved changes.
    
    JSdispatch <| SetHasUnsavedChanges false
    // set waveSim data
    dispatch <| SetWaveSimModel(name, waveSim)
    dispatch <| (
        {
            ProjectPath = dirName compToSetup.FilePath
            OpenFileName =  compToSetup.Name
            LoadedComponents = ldComps
        }
        |> SetProject) // this message actually changes the project in model
    dispatch <| SetWaveSimIsOutOfDate true
    dispatch <| SetIsLoading false 
    //printfn "Check 6..."
    

let updateLoadedComponents name (setFun: LoadedComponent -> LoadedComponent) (lcLst: LoadedComponent list) =
    let n = List.tryFindIndex (fun (lc: LoadedComponent) -> lc.Name = name) lcLst
    match n with
    | None -> 
        printf "In updateLoadedcomponents can't find name='%s' in components:%A" name lcLst
        lcLst
    | Some n ->
        let oldLc = lcLst.[n]
        let newLc = setFun oldLc
        writeComponentToBackupFile 0 1. oldLc
        List.mapi (fun i x -> if i = n then newLc else x) lcLst

/// return current project with current sheet updated from canvas if needed
let updateProjectFromCanvas (model:Model) =
    match model.Sheet.GetCanvasState() with
    | ([], []) -> model.CurrentProj
    | canvasState ->  
        canvasState
        |> fun canvas ->
            let inputs, outputs = parseDiagramSignature canvas
            let setLc lc =
                { lc with
                    CanvasState = canvas
                    InputLabels = inputs
                    OutputLabels = outputs
                }
            model.CurrentProj
            |> Option.map (fun p -> 
                {   
                    p with LoadedComponents = updateLoadedComponents p.OpenFileName setLc p.LoadedComponents
                })


/// extract SavedwaveInfo from model to be saved
let getSavedWave (model:Model) : SavedWaveInfo option = 
    match currWaveSimModel model with
    | Some wSModel -> waveSimModel2SavedWaveInfo wSModel |> Some
    | None -> None

/// add waveInfo to model
let setSavedWave compIds (wave: SavedWaveInfo option) model : Model =
    match wave, getCurrFile model with
    | None, _ -> model
    | Some waveInfo, Some fileName -> 
        { model with WaveSim = Map.add fileName (savedWaveInfo2WaveSimModel waveInfo) 
                                                (fst model.WaveSim), 
                               snd model.WaveSim }
    | Some waveInfo, _ -> model

/// Save the sheet currently open, return  the new sheet's Loadedcomponent if this has changed
let saveOpenFileAction isAuto model =
    match model.Sheet.GetCanvasState (), model.CurrentProj with
    | _, None -> None
    | canvasState, Some project ->
        // "DEBUG: Saving Sheet"
        // printfn "DEBUG: %A" project.ProjectPath
        // printfn "DEBUG: %A" project.OpenFileName
                
        let savedState = canvasState, getSavedWave model
        if isAuto then
            failwithf "Auto saving is no longer used"
            None
        else 
            saveStateToFile project.ProjectPath project.OpenFileName savedState
            removeFileWithExtn ".dgmauto" project.ProjectPath project.OpenFileName
            let origLdComp =
                project.LoadedComponents
                |> List.find (fun lc -> lc.Name = project.OpenFileName)
            let savedWaveSim =
                Map.tryFind project.OpenFileName (fst model.WaveSim)
                |> Option.map waveSimModel2SavedWaveInfo
            let newLdc, newState = makeLoadedComponentFromCanvasData canvasState origLdComp.FilePath DateTime.Now savedWaveSim, canvasState
            writeComponentToBackupFile 4 1. newLdc
            Some (newLdc,newState)
        
/// save current open file, updating model etc, and returning the loaded component and the saved (unreduced) canvas state
let saveOpenFileActionWithModelUpdate (model: Model) (dispatch: Msg -> Unit) =
    let opt = saveOpenFileAction false model
    let ldcOpt = Option.map fst opt
    let state = Option.map snd opt |> Option.defaultValue ([],[])
    match model.CurrentProj with
    | None -> failwithf "What? Should never be able to save sheet when project=None"
    | Some p -> 
        // update loaded components for saved file
        updateLdCompsWithCompOpt ldcOpt p.LoadedComponents
        |> (fun lc -> {p with LoadedComponents=lc})
        |> SetProject
        |> dispatch
        // update Autosave info
        SetLastSavedCanvas (p.OpenFileName, state)
        |> dispatch
    SetHasUnsavedChanges false
    |> JSDiagramMsg
    |> dispatch
    dispatch FinishUICmd
    opt

let private getFileInProject name project = project.LoadedComponents |> List.tryFind (fun comp -> comp.Name = name)

let private isFileInProject name project =
    getFileInProject name project
    |> function
    | None -> false
    | Some _ -> true

/// Create a new empty .dgm file and return corresponding loaded component.
let private createEmptyDiagramFile projectPath name =
    createEmptyDgmFile projectPath name

    {   
        Name = name
        TimeStamp = System.DateTime.Now
        WaveInfo = None
        FilePath = pathJoin [| projectPath; name + ".dgm" |]
        CanvasState = [],[]
        InputLabels = []
        OutputLabels = []
    }


let createEmptyComponentAndFile (pPath:string)  (sheetName: string) : LoadedComponent =
    createEmptyDgmFile pPath sheetName
    {
        Name=sheetName
        WaveInfo = None
        TimeStamp = DateTime.Now
        FilePath= pathJoin [|pPath; sprintf "%s.dgm" sheetName|]
        CanvasState=([],[])
        InputLabels = []
        OutputLabels = []
    }

/// Load a new project as defined by parameters.
/// Ends any existing simulation
/// Closes WaveSim if this is being used
let setupProjectFromComponents (sheetName: string) (ldComps: LoadedComponent list) (model: Model) (dispatch: Msg->Unit)=
    let compToSetup =
        match ldComps with
        | [] -> failwithf "setupProjectComponents must be called with at least one LoadedComponent"
        | comps ->
            // load sheetName
            match comps |> List.tryFind (fun comp -> comp.Name = sheetName) with
            | None -> failwithf "What? can't find sheet %s in loaded sheets %A" sheetName (comps |> List.map (fun c -> c.Name))
            | Some comp -> comp
    match model.CurrentProj with
    | None -> ()
    | Some p ->
        dispatch EndSimulation // Message ends any running simulation.
        // TODO: make each sheet wavesim remember the list of waveforms.
    let waveSim = 
        compToSetup.WaveInfo
        |> Option.map savedWaveInfo2WaveSimModel 
        |> Option.defaultValue (ModelType.initWS [||] Map.empty)

    // TODO
    loadStateIntoModel compToSetup waveSim ldComps model dispatch
    {
        ProjectPath = dirName compToSetup.FilePath
        OpenFileName =  compToSetup.Name
        LoadedComponents = ldComps
    }
    |> SetProject // this message actually changes the project in model
    |> dispatch

/// Open the specified file, saving the current file if needed.
/// Creates messages sufficient to do all necessary model and diagram change
/// Terminates a simulation if one is running
/// Closes waveadder if it is open
let private openFileInProject' saveCurrent name project (model:Model) dispatch =
    let newModel = {model with CurrentProj = Some project}
    match getFileInProject name project with
    | None -> 
        log <| sprintf "Warning: openFileInProject could not find the component %s in the project" name
    | Some lc ->
        match updateProjectFromCanvas model with
        | None -> failwithf "What? current project cannot be None at this point in openFileInProject"
        | Some p ->
            let updatedModel = {model with CurrentProj = Some p}
            let ldcs =
                if saveCurrent then 
                    let opt = saveOpenFileAction false updatedModel
                    let ldcOpt = Option.map fst opt
                    let ldComps = updateLdCompsWithCompOpt ldcOpt project.LoadedComponents
                    let reducedState = Option.map snd opt |> Option.defaultValue ([],[])
                    match model.CurrentProj with
                    | None -> failwithf "What? Should never be able to save sheet when project=None"
                    | Some p -> 
                        // update Autosave info
                        SetLastSavedCanvas (p.OpenFileName,reducedState)
                        |> dispatch
                    SetHasUnsavedChanges false
                    |> JSDiagramMsg
                    |> dispatch
                    ldComps
                else
                    project.LoadedComponents
            setupProjectFromComponents name ldcs newModel dispatch

let openFileInProject name project (model:Model) dispatch =
    openFileInProject' true name project (model:Model) dispatch
    dispatch FinishUICmd


/// return a react warning message if name if not valid for a sheet Add or Rename, or else None
let maybeWarning dialogText project =
    let redText txt = Some <| div [ Style [ Color "red" ] ] [ str txt ]
    if isFileInProject dialogText project then
        redText "This sheet already exists." 
    elif dialogText.StartsWith " " || dialogText.EndsWith " " then
        redText "The name cannot start or end with a space."
    elif String.exists ((=) '.') dialogText then
        redText "The name cannot contain a file suffix."
    elif not <| String.forall (fun c -> Char.IsLetterOrDigit c || c = ' ') dialogText then
        redText "The name must be alphanumeric."
    elif ((dialogText |> Seq.tryItem 0) |> Option.map Char.IsDigit) = Some true then
        redText "The name must not start with a digit"
    else None


/// rename a sheet
let renameSheet oldName newName (model:Model) dispatch =
    let saveAllFilesFromProject (proj: Project) =
        proj.LoadedComponents
        |> List.iter (fun ldc ->
            let name = ldc.Name
            let state = ldc.CanvasState
            let waveInfo = ldc.WaveInfo
            saveStateToFile proj.ProjectPath name (state,waveInfo)
            removeFileWithExtn ".dgmauto" proj.ProjectPath name)

    let renameComps oldName newName (comps:Component list) : Component list = 
        comps
        |> List.map (fun comp -> 
            match comp with 
            | {Type= Custom ({Name = compName} as customType)} when compName = oldName-> 
                {comp with Type = Custom {customType with Name = newName} }
            | c -> c)

    let renameCustomComponents newName (ldComp:LoadedComponent) =
        let state = ldComp.CanvasState
        {ldComp with CanvasState = renameComps oldName newName (fst state), snd state}

    let renameSheetsInProject oldName newName proj =
        {proj with
            OpenFileName = if proj.OpenFileName = oldName then newName else proj.OpenFileName
            LoadedComponents =
                proj.LoadedComponents
                |> List.map (fun ldComp -> 
                    match ldComp with
                    | {Name = lcName} when lcName = oldName -> 
                        {ldComp with Name=newName}
                    | _ ->
                        renameCustomComponents newName ldComp )
        }
    match updateProjectFromCanvas model with
    | None -> 
        failwithf "What? current project cannot be None at this point in renamesheet"
    | Some p ->
        let updatedModel = {model with CurrentProj = Some p}
        let opt = saveOpenFileAction false updatedModel
        let ldcOpt = Option.map fst opt
        let ldComps = updateLdCompsWithCompOpt ldcOpt p.LoadedComponents
        let reducedState = Option.map snd opt |> Option.defaultValue ([],[])
        // update Autosave info
        SetLastSavedCanvas (p.OpenFileName,reducedState)
        |> dispatch
        SetHasUnsavedChanges false
        |> JSDiagramMsg
        |> dispatch
        let proj' = renameSheetsInProject oldName newName p
        setupProjectFromComponents proj'.OpenFileName proj'.LoadedComponents model dispatch
        [".dgm";".dgmauto"] |> List.iter (fun extn -> renameFile extn proj'.ProjectPath oldName newName)
        /// save all the other files
        saveAllFilesFromProject proj'
        dispatch FinishUICmd

        
    


/// rename file
let renameFileInProject name project model dispatch =
    match model.CurrentProj, getCurrentWSMod model with
    | None,_ -> log "Warning: renameFileInProject called when no project is currently open"
    | Some project, Some ws when ws.WSViewState<>WSClosed ->
        displayFileErrorNotification "Sorry, you must close the wave simulator before renaming design sheets!" dispatch
        switchToWaveEditor model dispatch
    | Some project, _ ->
        // Prepare dialog popup.
        let title = "Rename sheet in project"

        let before =
            fun (dialogData: PopupDialogData) ->
                let dialogText = getText dialogData

                div []
                    [ 
                      str <| "Warning: the current sheet will be saved during this operation."
                      br []
                      str <| "Names of existing components in other sheets that use the renamed sheet will still reflect the old sheet name.";
                      str <| " You may change names manually if you wish, operation does not depend on the name."
                      br []; br []
                      str <| sprintf "Sheet %s will be renamed as %s:" name dialogText
                      br []; br []
                      //str <| dialogText + ".dgm"
                      Option.defaultValue (div [] []) (maybeWarning dialogText project)]

        let placeholder = "New name for design sheet"
        let body = dialogPopupBodyOnlyText before placeholder dispatch
        let buttonText = "Rename"

        let buttonAction =
            fun (dialogData: PopupDialogData) ->
                // Create empty file.
                let newName = (getText dialogData).ToLower()
                // rename the file in the project.
                renameSheet name newName model dispatch
                dispatch ClosePopup

        let isDisabled =
            fun (dialogData: PopupDialogData) ->
                let dialogText = getText dialogData
                (isFileInProject dialogText project) || (dialogText = "")

        dialogPopup title body buttonText buttonAction isDisabled dispatch



/// Remove file.
let private removeFileInProject name project model dispatch =
    match getCurrentWSMod model with
    | Some ws when ws.WSViewState<>WSClosed ->
        displayFileErrorNotification "Sorry, you must close the wave simulator before removing design sheets!" dispatch
        switchToWaveEditor model dispatch
    | _ ->
        
        removeFile project.ProjectPath name
        removeFile project.ProjectPath (name + "auto")
        // Remove the file from the dependencies and update project.
        let newComponents = List.filter (fun (lc: LoadedComponent) -> lc.Name.ToLower() <> name.ToLower()) project.LoadedComponents
        // Make sure there is at least one file in the project.
        let project' = {project with LoadedComponents = newComponents}
        match newComponents, name = project.OpenFileName with
        | [],true -> 
            let newComponents = [ (createEmptyDiagramFile project.ProjectPath "main") ]
            openFileInProject' false project.LoadedComponents.[0].Name project' model dispatch
        | [], false -> 
            failwithf "What? - this cannot happen"
        | nc, true ->
            openFileInProject' false project'.LoadedComponents.[0].Name project' model dispatch
        | nc, false ->
            // nothing chnages except LoadedComponents
            dispatch <| SetProject project'
        dispatch FinishUICmd

                

/// Create a new file in this project and open it automatically.
let addFileToProject model dispatch =
    match model.CurrentProj with
    | None -> log "Warning: addFileToProject called when no project is currently open"
    | Some project ->
        // Prepare dialog popup.
        let title = "Add sheet to project"

        let before =
            fun (dialogData: PopupDialogData) ->
                let dialogText = getText dialogData
                let warn = maybeWarning dialogText project
                div []
                    [ str "A new sheet will be created at:"
                      br []
                      str <| pathJoin
                                 [| project.ProjectPath
                                    dialogText + ".dgm" |]
                      Option.defaultValue (div [] []) warn ]

        let placeholder = "Insert design sheet name"
        let body = dialogPopupBodyOnlyText before placeholder dispatch
        let buttonText = "Add"
        let buttonAction =
            fun (dialogData: PopupDialogData) ->
                    // Create empty file.
                    let name = (getText dialogData).ToLower()
                    createEmptyDgmFile project.ProjectPath name
                    // Add the file to the project.
                    let newComponent = {
                        Name = name
                        TimeStamp = System.DateTime.Now
                        WaveInfo = None
                        FilePath = pathJoin [|project.ProjectPath; name + ".dgm"|]
                        CanvasState = [],[]
                        InputLabels = []
                        OutputLabels = []
                    }
                    let updatedProject =
                        { project with
                              LoadedComponents = newComponent :: project.LoadedComponents
                              OpenFileName = name }
 
                    // Open the file, updating the project, saving current file
                    openFileInProject' true name updatedProject model dispatch
                    // Close the popup.
                    dispatch ClosePopup
                    dispatch FinishUICmd

        let isDisabled =
            fun (dialogData: PopupDialogData) ->
                let dialogText = getText dialogData
                (isFileInProject dialogText project) || (dialogText = "") || (maybeWarning dialogText project <> None)

        dialogPopup title body buttonText buttonAction isDisabled dispatch

/// Close current project, if any.
let forceCloseProject model dispatch =
    dispatch (StartUICmd CloseProject)
    let sheetDispatch sMsg = dispatch (Sheet sMsg) 
    dispatch EndSimulation // End any running simulation.
    model.Sheet.ClearCanvas sheetDispatch
    dispatch FinishUICmd

let private closeProject model dispatch _ =
    let closeDialogButtons keepOpen _ =
        if keepOpen then
            dispatch ClosePopup
        else
            forceCloseProject model dispatch

    if model.SavedSheetIsOutOfDate then 
        choicePopup 
                "Close Project?" 
                (div [] [ str "The current file has unsaved changes."])
                "Go back to project" "Close project without saving changes"  
                closeDialogButtons 
                dispatch
    else
        forceCloseProject model dispatch


/// Create a new project.
let private newProject model dispatch _ =
    dispatch <| SetRouterInteractive false
    match askForNewProjectPath() with
    | None -> () // User gave no path.
    | Some path ->
        match tryCreateFolder path with
        | Error err ->
            log err
            let errMsg = "Could not create a folder for the project."
            displayFileErrorNotification errMsg dispatch
        | Ok _ ->
            dispatch EndSimulation // End any running simulation.
            // Create empty placeholder projectFile.
            let projectFile = baseName path + ".dprj"
            writeFile (pathJoin [| path; projectFile |]) ""
            // Create empty initial diagram file.
            let initialComponent = createEmptyComponentAndFile path "main"
            setupProjectFromComponents "main" [initialComponent] model dispatch










/// work out what to do opening a file
let rec resolveComponentOpenPopup 
        (pPath:string)
        (components: LoadedComponent list)  
        (resolves: LoadStatus list) 
        (model: Model)
        (dispatch: Msg -> Unit) =
    let chooseWhichToOpen comps =
        (List.maxBy (fun comp -> comp.TimeStamp) comps).Name
    dispatch ClosePopup
    match resolves with
    | [] -> setupProjectFromComponents (chooseWhichToOpen components) components model dispatch
    | Resolve (ldComp,autoComp) :: rLst ->
        // ldComp, autocomp are from attemps to load saved file and its autosave version.
        let compChanges, connChanges = quantifyChanges ldComp autoComp
        let buttonAction autoSave _ =
            let comp = {(if autoSave then autoComp else ldComp) with TimeStamp = DateTime.Now}
            writeComponentToFile comp
            if compChanges + connChanges > 0 then
                writeComponentToBackupFile 0 1. comp 
            resolveComponentOpenPopup pPath (comp :: components) rLst  model dispatch   
        // special case when autosave data is most recent
        let title = "Warning!"
        let message, color =
            match compChanges + connChanges with
            | 0 -> 
                sprintf "There were layout but no circuit changes made in sheet %s after your last save. \
                         There is an automatically saved version which is \
                         more uptodate. Do you want to keep the newer AutoSaved version or \
                         the older Saved version?"  ldComp.Name, "green"  
            | n when n < 3 ->   
                sprintf "Warning: %d component and %d connection changes were made to sheet '%s' after your last Save. \
                         There is an automatically saved version which is \
                         more uptodate. Do you want to keep the newer AutoSaved version or \
                         the older saved version?"  compChanges connChanges ldComp.Name, "orange"
            | n -> 
                sprintf "Warning: %d component and %d connection changes were made to sheet '%s' after your last Save. \
                         There is an automatically saved version which is \
                         more uptodate. Do you want to keep the newer AutoSaved version or \
                         the older saved version? This is a large change so the option you do not choose \
                         will be saved as file 'backup/%s.dgm'"  compChanges connChanges ldComp.Name ldComp.Name, "red"
        let body = 
            div [Style [Color color]] [str message] 
        choicePopup title body "Newer AutoSaved file" "Older Saved file" buttonAction dispatch
    | OkAuto autoComp :: rLst ->
         let errMsg = "Could not load saved project file '%s' - using autosave file instead"
         displayFileErrorNotification errMsg dispatch
         resolveComponentOpenPopup pPath (autoComp::components) rLst model dispatch
    | OkComp comp ::rLst -> 
        resolveComponentOpenPopup pPath (comp::components) rLst model dispatch
 

/// open an existing project
let private openProject model dispatch _ =
    match askForExistingProjectPath () with
    | None -> () // User gave no path.
    | Some path ->
        traceIf "project" (fun () -> "loading files")
        match loadAllComponentFiles path with
        | Error err ->
            log err
            displayFileErrorNotification err dispatch
        | Ok componentsToResolve ->
            traceIf "project " (fun () -> "resolving popups...")
            resolveComponentOpenPopup path [] componentsToResolve model dispatch
            traceIf "project" (fun () ->  "project successfully opened.")

/// Display the initial Open/Create Project menu at the beginning if no project
/// is open.
let viewNoProjectMenu model dispatch =
    let menuItem label action =
        Menu.Item.li
            [ Menu.Item.IsActive false
              Menu.Item.OnClick action ] [ str label ]

    let initialMenu =
        Menu.menu []
            [ Menu.list []
                  [ menuItem "New project" (newProject model dispatch)
                    menuItem "Open project" (openProject model dispatch) ]
            ]

    match model.CurrentProj with
    | Some _ -> div [] []
    | None -> unclosablePopup None initialMenu None []

//TODO ASK WHY WE NEED TO DO THIS _ VARIABLE FOR IT TO WORK?
//These two functions deal with the fact that there is a type error otherwise..
let goBackToProject model dispatch _ =
    dispatch (SetExitDialog false)

let closeApp model dispatch _ =
    dispatch CloseApp

/// Display the exit dialog
let viewExitDialog model (dispatch : Msg -> unit) =
    let menuItem label action =
        Menu.Item.li
            [ Menu.Item.IsActive false
              Menu.Item.OnClick action ] [ str label ]

    let exitMenu =
        Menu.menu []
            [ Menu.label [] [str "You have unsaved changes"]
              Menu.list []
                  [ menuItem "Go back to project" (goBackToProject model dispatch)
                    menuItem "Close without saving" (closeApp model dispatch) ]
            ]

    if model.ExitDialog then unclosablePopup None exitMenu None []
    else div [] []



/// Display top menu.
let viewTopMenu model messagesFunc simulateButtonFunc dispatch =
    let compIds = getComponentIds model
    
    messagesFunc model dispatch

    //printfn "FileView"
    let style = Style [ Width "100%" ] //leftSectionWidth model

    let projectPath, fileName =
        match model.CurrentProj with
        | None -> "no open project", "no open sheet"
        | Some project -> project.ProjectPath, project.OpenFileName

    let makeFileLine name project =
        Navbar.Item.div [ Navbar.Item.Props [ style ] ]
            [ Level.level [ Level.Level.Props [ style ] ]
                  [ Level.left [] [ Level.item [] [ str name ] ]
                    Level.right [ Props [ Style [ MarginLeft "20px" ] ] ]
                        [ Level.item []
                              [ Button.button
                                  [ Button.Size IsSmall
                                    Button.IsOutlined
                                    Button.Color IsPrimary
                                    Button.Disabled(name = project.OpenFileName)
                                    Button.OnClick(fun _ ->
                                        dispatch (StartUICmd ChangeSheet)
                                        openFileInProject name project model dispatch) ] [ str "open" ] 
                          ]
                          // Add option to rename?
                          Level.item [] [
                              Button.button [
                                  Button.Size IsSmall
                                  Button.IsOutlined
                                  Button.Color IsInfo
                                  Button.OnClick(fun _ ->
                                      dispatch (StartUICmd RenameSheet)
                                      renameFileInProject name project model dispatch) ] [ str "rename" ]
                          ]
                          Level.item []
                              [ Button.button
                                  [ Button.Size IsSmall
                                    Button.IsOutlined
                                    Button.Color IsDanger
                                    Button.OnClick(fun _ ->
                                        let title = "Delete sheet"

                                        let body =
                                            div []
                                                [ str "Are you sure you want to delete the following design sheet?"
                                                  br []
                                                  str <| pathJoin
                                                             [| project.ProjectPath
                                                                name + ".dgm" |]
                                                  br []
                                                  str <| "This action is irreversible." ]

                                        let buttonText = "Delete"

                                        let buttonAction =
                                            fun _ ->
                                                dispatch (StartUICmd DeleteSheet)
                                                removeFileInProject name project model dispatch
                                                dispatch ClosePopup
                                        confirmationPopup title body buttonText buttonAction dispatch) ]
                                    [ str "delete" ] ] ] ] ]

    let fileTab =
        match model.CurrentProj with
        | None -> Navbar.Item.div [] []
        | Some project ->
            let projectFiles = project.LoadedComponents |> List.map (fun comp -> makeFileLine comp.Name project)
            Navbar.Item.div
                [ Navbar.Item.HasDropdown
                  Navbar.Item.Props
                      [ OnClick(fun _ ->
                          if model.TopMenuOpenState = Files then Closed else Files
                          |> SetTopMenu
                          |> dispatch) ] ]
                [ Navbar.Link.a [] [ str "Sheets" ]
                  Navbar.Dropdown.div
                      [ Navbar.Dropdown.Props
                          [ Style
                              [ Display
                                  (if (let b = model.TopMenuOpenState = Files
                                       b) then
                                      DisplayOptions.Block
                                   else
                                      DisplayOptions.None) ] ] ]
                      ([ Navbar.Item.a [ Navbar.Item.Props [ OnClick(fun _ -> 
                            dispatch (StartUICmd AddSheet)
                            addFileToProject model dispatch) ] ]
                             [ str "New Sheet" ]
                         Navbar.divider [] [] ]
                       @ projectFiles) ]

    div [   HTMLAttr.Id "TopMenu"
            leftSectionWidth model
            Style [ Position PositionOptions.Absolute
                    UserSelect UserSelectOptions.None ]
        ]
        [ Navbar.navbar
            [ Navbar.Props
                [  Style
                    [ Height "100%"
                      Width "100%" ] ] ]
              [ Navbar.Brand.div
                  [ Props
                      [ Style
                          [ Height "100%"
                            Width "100%" ] ] ]
                    [ Navbar.Item.div
                        [ Navbar.Item.HasDropdown
                          Navbar.Item.Props
                              [ OnClick(fun _ ->
                                  if model.TopMenuOpenState = Project then Closed else Project
                                  |> SetTopMenu
                                  |> dispatch) ] ]
                          [ Navbar.Link.a [] [ str "Project" ]
                            Navbar.Dropdown.div
                                [ Navbar.Dropdown.Props
                                    [ Style
                                        [ Display
                                            (if model.TopMenuOpenState = Project then
                                                DisplayOptions.Block
                                             else
                                                 DisplayOptions.None) ] ] ]
                                [ Navbar.Item.a [ Navbar.Item.Props [ OnClick <| newProject model dispatch ] ]
                                      [ str "New project" ]
                                  Navbar.Item.a [ Navbar.Item.Props [ OnClick <| openProject model dispatch ] ]
                                      [ str "Open project" ]
                                  Navbar.Item.a [ Navbar.Item.Props [ OnClick <| closeProject model dispatch ] ]
                                      [ str "Close project" ] ] ]

                      fileTab
                      Navbar.Item.div []
                          [ Navbar.Item.div []
                                [ Breadcrumb.breadcrumb [ Breadcrumb.HasArrowSeparator ]
                                      [ Breadcrumb.item [] [ str <| cropToLength 30 false projectPath ]
                                        Breadcrumb.item [] [ span [ Style [ FontWeight "bold" ] ] [ str fileName ] ] ] ] ]
                      Navbar.Item.div []
                          [ Navbar.Item.div []
                                [ Button.button
                                    ((if model.SavedSheetIsOutOfDate then 
                                        []
                                       else
                                        [ Button.Color IsLight ]) @
                                    [
                                      Button.Color IsSuccess  
                                      
                                      Button.OnClick(fun _ -> 
                                        dispatch (StartUICmd SaveSheet)
                                        saveOpenFileActionWithModelUpdate model dispatch |> ignore
                                        dispatch <| Sheet(Sheet.DoNothing) //To update the savedsheetisoutofdate send a sheet message
                                        ) ]) [ str "Save" ] ] ]
                      Navbar.End.div []
                          [ 
                            Navbar.Item.div [] 
                                [ simulateButtonFunc compIds model dispatch ] ]
                      Navbar.End.div []
                          [ Navbar.Item.div []
                                [ Button.button 
                                    [ Button.OnClick(fun _ -> PopupView.viewInfoPopup dispatch) 
                                      Button.Color IsInfo
                                    ] [ str "Info" ] ] ] ] ] ]
