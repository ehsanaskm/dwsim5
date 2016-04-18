'    Property Package Base Class
'    Copyright 2008-2014 Daniel Wagner O. de Medeiros
'
'    This file is part of DWSIM.
'
'    DWSIM is free software: you can redistribute it and/or modify
'    it under the terms of the GNU General Public License as published by
'    the Free Software Foundation, either version 3 of the License, or
'    (at your option) any later version.
'
'    DWSIM is distributed in the hope that it will be useful,
'    but WITHOUT ANY WARRANTY; without even the implied warranty of
'    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
'    GNU General Public License for more details.
'
'    You should have received a copy of the GNU General Public License
'    along with DWSIM.  If not, see <http://www.gnu.org/licenses/>.

Imports System.Runtime.Serialization.Formatters.Binary
Imports System.Runtime.Serialization
Imports System.IO
Imports System.Linq
Imports System.Math
Imports CapeOpen
Imports System.Runtime.InteropServices.ComTypes
Imports iop = System.Runtime.InteropServices

Imports System.Xml.Serialization
Imports System.Runtime.Serialization.Formatters
Imports System.Threading.Tasks
Imports DWSIM.Thermodynamics.MathEx
Imports DWSIM.Thermodynamics.PropertyPackages.Auxiliary.FlashAlgorithms
Imports DWSIM.Interfaces
Imports DWSIM.Interfaces.Interfaces2

Namespace PropertyPackages

#Region "    Global Enumerations"

    Public Enum Phase

        Liquid
        Liquid1
        Liquid2
        Liquid3
        Aqueous
        Vapor
        Solid
        Mixture

    End Enum

    Public Enum State
        Liquid = 0
        Vapor = 1
        Solid = 2
    End Enum

    Public Enum FlashSpec

        P = 0
        T = 1
        S = 2
        H = 3
        V = 4
        U = 5
        VAP = 6
        SF = 7

    End Enum

    Public Enum ThermoProperty
        ActivityCoefficient
        Fugacity
        FugacityCoefficient
    End Enum

    Public Enum PackageType
        EOS = 0
        ActivityCoefficient = 1
        ChaoSeader = 2
        VaporPressure = 3
        Miscelaneous = 4
        CorrespondingStates = 5
        CAPEOPEN = 6
    End Enum

    Public Enum FlashMethod
        GlobalSetting = 2
        DWSIMDefault = 0
        InsideOut = 1
        InsideOut3P = 3
        GibbsMin2P = 4
        GibbsMin3P = 5
        NestedLoops3P = 6
        NestedLoopsSLE = 7
        NestedLoopsImmiscible = 8
        SimpleLLE = 9
        NestedLoopsSLE_SS = 10
        NestedLoops3PV2 = 11
        NestedLoops3PV3 = 12
    End Enum

    Public Enum Parameter
        PHFlash_Internal_Loop_Tolerance = 0
        PSFlash_Internal_Loop_Tolerance = 1
        PHFlash_External_Loop_Tolerance = 2
        PSFlash_External_Loop_Tolerance = 3
        PHFlash_Maximum_Number_Of_External_Iterations = 4
        PSFlash_Maximum_Number_Of_External_Iterations = 5
        PHFlash_Maximum_Number_Of_Internal_Iterations = 6
        PSFlash_Maximum_Number_Of_Internal_Iterations = 7
        PTFlash_Maximum_Number_Of_External_Iterations = 8
        PTFlash_Maximum_Number_Of_Internal_Iterations = 9
        PTFlash_External_Loop_Tolerance = 10
        PTFlash_Internal_Loop_Tolerance = 11
    End Enum

#End Region

    ''' <summary>
    ''' The Property Package Class contains methods to do thermodynamic calculations for all supported phases in DWSIM.
    ''' </summary>
    ''' <remarks>The base class is inherited by each implemented property package, which contains its own methods.</remarks>
    <System.Serializable()> Public MustInherit Class PropertyPackage

        'CAPE-OPEN 1.0 Interfaces
        Implements ICapeIdentification, ICapeThermoPropertyPackage, ICapeUtilities, ICapeThermoEquilibriumServer, ICapeThermoCalculationRoutine

        'CAPE-OPEN 1.1 Interfaces
        Implements ICapeThermoPhases, ICapeThermoPropertyRoutine, ICapeThermoCompounds, ICapeThermoUniversalConstant
        Implements ICapeThermoMaterialContext, ICapeThermoEquilibriumRoutine

        'CAPE-OPEN Persistence Interface
        Implements IPersistStreamInit

        'CAPE-OPEN Error Interfaces
        Implements ECapeUser, ECapeUnknown, ECapeRoot

        'IDisposable
        Implements IDisposable

        'DWSIM XML support
        Implements XMLSerializer.Interfaces.ICustomXMLSerialization

        'DWSIM IPropertyPackage
        Implements IPropertyPackage

        Public Const ClassId As String = ""

        <System.NonSerialized()> Private m_ms As Interfaces.IMaterialStream = Nothing
        Private m_ss As New System.Collections.Generic.List(Of String)
        Private m_configurable As Boolean = False

        Public m_par As New System.Collections.Generic.Dictionary(Of String, Double)
        Public m_Henry As New System.Collections.Generic.Dictionary(Of String, HenryParam)

        <NonSerialized> Private m_ip As DataTable

        Private _flashalgorithm As FlashMethod

        Public _packagetype As PackageType

        <System.NonSerialized()> Public _brio3 As Auxiliary.FlashAlgorithms.BostonFournierInsideOut3P
        <System.NonSerialized()> Public _bbio As Auxiliary.FlashAlgorithms.BostonBrittInsideOut
        <System.NonSerialized()> Public _dwdf As Auxiliary.FlashAlgorithms.DWSIMDefault
        <System.NonSerialized()> Public _gm3 As Auxiliary.FlashAlgorithms.GibbsMinimization3P
        <System.NonSerialized()> Public _nl3 As Auxiliary.FlashAlgorithms.NestedLoops3PV3
        <System.NonSerialized()> Public _nlsle As Auxiliary.FlashAlgorithms.NestedLoopsSLE
        <System.NonSerialized()> Public _nli As Auxiliary.FlashAlgorithms.NestedLoopsImmiscible
        <System.NonSerialized()> Public _simplelle As Auxiliary.FlashAlgorithms.SimpleLLE

        Public _tpseverity As Integer = 0
        Public _tpcompids As String() = New String() {}

        Public _phasemappings As New Dictionary(Of String, PhaseInfo)

        Public IsElectrolytePP As Boolean = False

        Private LoopVarF, LoopVarX As Double, LoopVarState As State

        Public Property ForceNewFlashAlgorithmInstance As Boolean = False

        <System.NonSerialized()> Private _como As Object 'CAPE-OPEN Material Object

        '<System.NonSerialized()> Public ConfigForm As FormConfigBase 'System.Windows.Forms.Form

#Region "   Constructor"

        Sub New()

            Initialize()

        End Sub

        Sub New(ByVal capeopenmode As Boolean)

            Calculator.CAPEOPENMode = capeopenmode

            If capeopenmode Then

                'initialize collections

                _selectedcomps = New Dictionary(Of String, Interfaces.ICompoundConstantProperties)
                _availablecomps = New Dictionary(Of String, Interfaces.ICompoundConstantProperties)

                'load databases

            End If

            Initialize()

        End Sub

        Public Shared Function ReturnInstance(typename As String) As PropertyPackage

            If typename.StartsWith("PropertyPackage") Then typename = typename.Insert(0, "DWSIM.Thermodynamics.")
            Dim t As Type = Type.GetType(typename, False)
            Return Activator.CreateInstance(t)

        End Function

        Public Overridable Sub ConfigParameters()
            m_par = New System.Collections.Generic.Dictionary(Of String, Double)
            With Me.Parameters
                .Clear()
                .Add("PP_PHFILT", 0.001)
                .Add("PP_PSFILT", 0.001)
                .Add("PP_PHFELT", 0.001)
                .Add("PP_PSFELT", 0.001)
                .Add("PP_PHFMEI", 50)
                .Add("PP_PSFMEI", 50)
                .Add("PP_PHFMII", 100)
                .Add("PP_PSFMII", 100)
                .Add("PP_PTFMEI", 100)
                .Add("PP_PTFMII", 100)
                .Add("PP_PTFILT", 0.001)
                .Add("PP_PTFELT", 0.001)
                .Add("PP_FLASHALGORITHM", 2)
                .Add("PP_FLASHALGORITHMFASTMODE", 1)
                .Add("PP_FLASHALGORITHMIDEALKFALLBACK", 1)
                .Add("PP_IDEAL_MIXRULE_LIQDENS", 0)
                .Add("PP_USEEXPLIQDENS", 0)
            End With
        End Sub

        Public Sub CreatePhaseMappings()
            Me._phasemappings = New Dictionary(Of String, PhaseInfo)
            With Me._phasemappings
                .Add("Vapor", New PhaseInfo("", 2, Phase.Vapor))
                .Add("Liquid1", New PhaseInfo("", 3, Phase.Liquid1))
                .Add("Liquid2", New PhaseInfo("", 4, Phase.Liquid2))
                .Add("Liquid3", New PhaseInfo("", 5, Phase.Liquid3))
                .Add("Aqueous", New PhaseInfo("", 6, Phase.Aqueous))
                .Add("Solid", New PhaseInfo("", 7, Phase.Solid))
            End With
        End Sub

        Public Sub CreatePhaseMappingsDW()
            Me._phasemappings = New Dictionary(Of String, PhaseInfo)
            With Me._phasemappings
                .Add("Vapor", New PhaseInfo("Vapor", 2, Phase.Vapor))
                .Add("Liquid1", New PhaseInfo("Liquid", 3, Phase.Liquid1))
                .Add("Liquid2", New PhaseInfo("Liquid2", 4, Phase.Liquid2))
                .Add("Solid", New PhaseInfo("Solid", 7, Phase.Solid))
            End With
        End Sub

#End Region

#Region "   Properties"

        Public ReadOnly Property PhaseMappings() As Dictionary(Of String, PhaseInfo)
            Get
                If Me._phasemappings Is Nothing Then
                    CreatePhaseMappingsDW()
                ElseIf Me._phasemappings.Count = 0 Then
                    CreatePhaseMappingsDW()
                End If
                Return _phasemappings
            End Get
        End Property

        ''' <summary>
        ''' Returns the flash algorithm selected for this property package.
        ''' </summary>
        ''' <value></value>
        ''' <returns>A FlashMethod value with information about the selected flash algorithm.</returns>
        ''' <remarks></remarks>
        Public Property FlashAlgorithm() As FlashMethod
            Get
                If Calculator.CAPEOPENMode Then
                    Return _flashalgorithm
                Else
                    If _flashalgorithm = FlashMethod.GlobalSetting Then
                        If Me.CurrentMaterialStream.Flowsheet Is Nothing Then
                            Return FlashMethod.DWSIMDefault
                        Else
                            Return Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.PropertyPackageFlashAlgorithm
                        End If
                    Else
                        Return _flashalgorithm
                    End If
                End If
            End Get
            Set(ByVal value As FlashMethod)
                _flashalgorithm = value
            End Set
        End Property

        ''' <summary>
        ''' Returns the FlashAlgorithm object instance for this property package.
        ''' </summary>
        ''' <value></value>
        ''' <returns>A FlashAlgorithm object to be used in flash calculations.</returns>
        ''' <remarks></remarks>
        Public Overridable ReadOnly Property FlashBase() As Auxiliary.FlashAlgorithms.FlashAlgorithm
            Get
                If Not Calculator.CAPEOPENMode Then
                    If Not Me.Parameters.ContainsKey("PP_FLASHALGORITHM") Then
                        Me.Parameters.Add("PP_FLASHALGORITHM", 2)
                    End If
                    Me.FlashAlgorithm = Me.Parameters("PP_FLASHALGORITHM")
                End If
                Select Case FlashAlgorithm
                    Case FlashMethod.DWSIMDefault
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.DWSIMDefault
                        'Else
                        '    If _dwdf Is Nothing Then _dwdf = New Auxiliary.FlashAlgorithms.DWSIMDefault
                        '    Return _dwdf
                        'End If
                    Case FlashMethod.InsideOut
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.BostonBrittInsideOut
                        'Else
                        '    If _bbio Is Nothing Then _bbio = New Auxiliary.FlashAlgorithms.BostonBrittInsideOut
                        '    Return _bbio
                        'End If
                    Case FlashMethod.InsideOut3P
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.BostonFournierInsideOut3P With
                                                    {.StabSearchCompIDs = _tpcompids, .StabSearchSeverity = _tpseverity}
                        'Else
                        'If _brio3 Is Nothing Then _brio3 = New Auxiliary.FlashAlgorithms.BostonFournierInsideOut3P
                        '_brio3.StabSearchCompIDs = _tpcompids
                        '_brio3.StabSearchSeverity = _tpseverity
                        'Return _brio3
                        'End If
                    Case FlashMethod.GibbsMin2P
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.GibbsMinimization3P With
                                                    {.ForceTwoPhaseOnly = True}
                        'Else
                        'If _gm3 Is Nothing Then _gm3 = New Auxiliary.FlashAlgorithms.GibbsMinimization3P With {.ForceTwoPhaseOnly = True}
                        'Return _gm3
                        'End If
                    Case FlashMethod.GibbsMin3P
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.GibbsMinimization3P With
                                                    {.ForceTwoPhaseOnly = False, .StabSearchCompIDs = _tpcompids, .StabSearchSeverity = _tpseverity}
                        'Else
                        'If _gm3 Is Nothing Then _gm3 = New Auxiliary.FlashAlgorithms.GibbsMinimization3P
                        '_gm3.ForceTwoPhaseOnly = False
                        '_gm3.StabSearchCompIDs = _tpcompids
                        '_gm3.StabSearchSeverity = _tpseverity
                        'Return _gm3
                        'End If
                    Case FlashMethod.NestedLoops3P, FlashMethod.NestedLoops3PV2, FlashMethod.NestedLoops3PV3
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.NestedLoops3PV3 With
                                                    {.StabSearchCompIDs = _tpcompids, .StabSearchSeverity = _tpseverity}
                        'Else
                        'If _nl3 Is Nothing Then _nl3 = New Auxiliary.FlashAlgorithms.NestedLoops3PV3
                        '_nl3.StabSearchCompIDs = _tpcompids
                        '_nl3.StabSearchSeverity = _tpseverity
                        'Return _nl3
                        'End If
                    Case FlashMethod.NestedLoopsSLE
                        Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                        For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                            constprops.Add(su.ConstantProperties)
                        Next
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.NestedLoopsSLE With {.CompoundProperties = constprops}
                        'Else
                        'If _nlsle Is Nothing Then _nlsle = New Auxiliary.FlashAlgorithms.NestedLoopsSLE
                        '_nlsle.CompoundProperties = constprops
                        'Return _nlsle
                        'End If
                    Case FlashMethod.NestedLoopsSLE_SS
                        Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                        For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                            constprops.Add(su.ConstantProperties)
                        Next
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.NestedLoopsSLE With {.CompoundProperties = constprops, .SolidSolution = True}
                        'Else
                        'If _nlsle Is Nothing Then _nlsle = New Auxiliary.FlashAlgorithms.NestedLoopsSLE
                        '_nlsle.CompoundProperties = constprops
                        '_nlsle.SolidSolution = True
                        'Return _nlsle
                        'End If
                    Case FlashMethod.NestedLoopsImmiscible
                        Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                        For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                            constprops.Add(su.ConstantProperties)
                        Next
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.NestedLoopsImmiscible With
                                                    {.CompoundProperties = constprops, .StabSearchCompIDs = _tpcompids, .StabSearchSeverity = _tpseverity}
                        'Else
                        'If _nli Is Nothing Then _nli = New Auxiliary.FlashAlgorithms.NestedLoopsImmiscible
                        '_nli.CompoundProperties = constprops
                        '_nli.StabSearchCompIDs = _tpcompids
                        '_nli.StabSearchSeverity = _tpseverity
                        'Return _nli
                        'End If
                    Case FlashMethod.SimpleLLE
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.SimpleLLE
                        'Else
                        'If _simplelle Is Nothing Then _simplelle = New Auxiliary.FlashAlgorithms.SimpleLLE
                        'Return _simplelle
                        'End If
                    Case Else
                        'If Calculator.IsRunningParallelTasks Or ForceNewFlashAlgorithmInstance Then
                        Return New Auxiliary.FlashAlgorithms.DWSIMDefault
                        'Else
                        'If _dwdf Is Nothing Then _dwdf = New Auxiliary.FlashAlgorithms.DWSIMDefault
                        'Return _dwdf
                        'End If
                End Select
            End Get
        End Property

        Public Property UniqueID() As String Implements IPropertyPackage.UniqueID

        Public Property Tag() As String Implements IPropertyPackage.Tag

        Public ReadOnly Property PackageType() As PackageType
            Get
                Return _packagetype
            End Get
        End Property

        Public Property ParametrosDeInteracao() As DataTable
            Get
                Return m_ip
            End Get
            Set(ByVal value As DataTable)
                m_ip = value
            End Set
        End Property

        Public ReadOnly Property Parameters() As Dictionary(Of String, Double)
            Get
                If Me.m_par Is Nothing Then ConfigParameters()
                Return Me.m_par
            End Get
        End Property

        Public Property IsConfigurable() As Boolean
            Get
                Return m_configurable
            End Get
            Set(ByVal value As Boolean)
                m_configurable = value
            End Set
        End Property

        ''' <summary>
        ''' Gets or sets the current material stream for this property package.
        ''' </summary>
        ''' <value></value>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Property CurrentMaterialStream() As Interfaces.IMaterialStream Implements Interfaces.IPropertyPackage.CurrentMaterialStream
            Get
                Return m_ms
            End Get
            Set(ByVal MatStr As Interfaces.IMaterialStream)
                m_ms = MatStr
            End Set
        End Property

        Public ReadOnly Property SupportedComponents() As System.Collections.Generic.List(Of String)
            Get
                Return m_ss
            End Get
        End Property

#End Region

#Region "   Cloning Support"

        Public Overridable Function Clone() As PropertyPackage

            Return ObjectCopy(Me)

        End Function

        Function ObjectCopy(ByVal obj As Object) As Object

            Dim objMemStream As New MemoryStream()
            Dim objBinaryFormatter As New BinaryFormatter(Nothing, New StreamingContext(StreamingContextStates.Clone))

            objBinaryFormatter.Serialize(objMemStream, obj)

            objMemStream.Seek(0, SeekOrigin.Begin)

            ObjectCopy = objBinaryFormatter.Deserialize(objMemStream)

            objMemStream.Close()
        End Function
#End Region

#Region "   Must Override or Overridable Functions"

        Public Overridable Sub ReconfigureConfigForm()

        End Sub

        ''' <summary>
        ''' Provides a wrapper function for CAPE-OPEN CalcProp/CalcSingleProp functions.
        ''' </summary>
        ''' <param name="property">The property to be calculated.</param>
        ''' <param name="phase">The phase where the property must be calculated for.</param>
        ''' <remarks>This function is necessary since DWSIM's internal property calculation function calculates all properties at once,
        ''' while the CAPE-OPEN standards require that only the property that was asked for to be calculated, leaving the others unchanged.</remarks>
        Public MustOverride Sub DW_CalcProp(ByVal [property] As String, ByVal phase As Phase)

        ''' <summary>
        ''' Provides a default implementation for solid phase property calculations in CAPE-OPEN mode. Should be used by all derived propety packages.
        ''' </summary>
        ''' <remarks></remarks>
        Public Overridable Sub DW_CalcSolidPhaseProps()

            Dim phaseID As Integer = 7
            Dim result As Double = 0.0#

            Dim T, P As Double
            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

            Dim phasemolarfrac As Double = Nothing
            Dim overallmolarflow As Double = Nothing

            overallmolarflow = Me.CurrentMaterialStream.Phases(0).Properties.molarflow.GetValueOrDefault
            phasemolarfrac = Me.CurrentMaterialStream.Phases(phaseID).Properties.molarfraction.GetValueOrDefault
            result = overallmolarflow * phasemolarfrac
            Me.CurrentMaterialStream.Phases(phaseID).Properties.molarflow = result
            result = result * Me.AUX_MMM(Phase.Solid) / 1000
            Me.CurrentMaterialStream.Phases(phaseID).Properties.massflow = result
            result = phasemolarfrac * overallmolarflow * Me.AUX_MMM(Phase.Solid) / 1000 / Me.CurrentMaterialStream.Phases(0).Properties.massflow.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(phaseID).Properties.massfraction = result

            result = Me.AUX_SOLIDDENS
            Me.CurrentMaterialStream.Phases(phaseID).Properties.density = result
            Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
            For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                constprops.Add(su.ConstantProperties)
            Next
            result = Me.DW_CalcSolidEnthalpy(T, RET_VMOL(PropertyPackages.Phase.Solid), constprops)
            Me.CurrentMaterialStream.Phases(phaseID).Properties.enthalpy = result
            Me.CurrentMaterialStream.Phases(phaseID).Properties.entropy = result / T
            Me.CurrentMaterialStream.Phases(phaseID).Properties.compressibilityFactor = 0.0#
            result = Me.DW_CalcSolidHeatCapacityCp(T, RET_VMOL(PropertyPackages.Phase.Solid), constprops)
            Me.CurrentMaterialStream.Phases(phaseID).Properties.heatCapacityCp = result
            Me.CurrentMaterialStream.Phases(phaseID).Properties.heatCapacityCv = result
            result = Me.AUX_MMM(Phase.Solid)
            Me.CurrentMaterialStream.Phases(phaseID).Properties.molecularWeight = result
            result = Me.CurrentMaterialStream.Phases(phaseID).Properties.enthalpy.GetValueOrDefault * Me.CurrentMaterialStream.Phases(phaseID).Properties.molecularWeight.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_enthalpy = result
            result = Me.CurrentMaterialStream.Phases(phaseID).Properties.entropy.GetValueOrDefault * Me.CurrentMaterialStream.Phases(phaseID).Properties.molecularWeight.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_entropy = result
            Me.CurrentMaterialStream.Phases(phaseID).Properties.thermalConductivity = 0.0#
            Me.CurrentMaterialStream.Phases(phaseID).Properties.viscosity = 1.0E+20
            Me.CurrentMaterialStream.Phases(phaseID).Properties.kinematic_viscosity = 1.0E+20

        End Sub

        ''' <summary>
        ''' Calculates the enthalpy of a mixture.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the mixture.</param>
        ''' <param name="T">Temperature (K)</param>
        ''' <param name="P">Pressure (Pa)</param>
        ''' <param name="st">State enum indicating the state of the mixture (liquid or vapor).</param>
        ''' <returns>The enthalpy of the mixture in kJ/kg.</returns>
        ''' <remarks>The basis for the calculated enthalpy/entropy in DWSIM is zero at 25 C and 1 atm.</remarks>
        Public MustOverride Function DW_CalcEnthalpy(ByVal Vx As Array, ByVal T As Double, ByVal P As Double, ByVal st As State) As Double

        ''' <summary>
        ''' Calculates the enthalpy departure of a mixture.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the mixture.</param>
        ''' <param name="T">Temperature (K)</param>
        ''' <param name="P">Pressure (Pa)</param>
        ''' <param name="st">State enum indicating the state of the mixture (liquid or vapor).</param>
        ''' <returns>The enthalpy departure of the mixture in kJ/kg.</returns>
        ''' <remarks>The basis for the calculated enthalpy/entropy in DWSIM is zero at 25 C and 1 atm.</remarks>
        Public MustOverride Function DW_CalcEnthalpyDeparture(ByVal Vx As Array, ByVal T As Double, ByVal P As Double, ByVal st As State) As Double

        ''' <summary>
        ''' Calculates the entropy of a mixture.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the mixture.</param>
        ''' <param name="T">Temperature (K)</param>
        ''' <param name="P">Pressure (Pa)</param>
        ''' <param name="st">State enum indicating the state of the mixture (liquid or vapor).</param>
        ''' <returns>The entropy of the mixture in kJ/kg.K.</returns>
        ''' <remarks>The basis for the calculated enthalpy/entropy in DWSIM is zero at 25 C and 1 atm.</remarks>
        Public MustOverride Function DW_CalcEntropy(ByVal Vx As Array, ByVal T As Double, ByVal P As Double, ByVal st As State) As Double

        ''' <summary>
        ''' Calculates the entropy departure of a mixture.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the mixture.</param>
        ''' <param name="T">Temperature (K)</param>
        ''' <param name="P">Pressure (Pa)</param>
        ''' <param name="st">State enum indicating the state of the mixture (liquid or vapor).</param>
        ''' <returns>The entropy departure of the mixture in kJ/kg.K.</returns>
        ''' <remarks>The basis for the calculated enthalpy/entropy in DWSIM is zero at 25 C and 1 atm.</remarks>
        Public MustOverride Function DW_CalcEntropyDeparture(ByVal Vx As Array, ByVal T As Double, ByVal P As Double, ByVal st As State) As Double

        ''' <summary>
        ''' Calculates K-values of components in a mixture.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the liquid phase.</param>
        ''' <param name="Vy">Vector of doubles containing the molar composition of the vapor phase.</param>
        ''' <param name="T">Temperature of the system.</param>
        ''' <param name="P">Pressure of the system.</param>
        ''' <returns>An array containing K-values for all components in the mixture.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public Overridable Overloads Function DW_CalcKvalue(ByVal Vx As System.Array, ByVal Vy As System.Array, ByVal T As Double, ByVal P As Double, Optional ByVal type As String = "LV") As Double()

            Dim fugvap As Double() = Nothing
            Dim fugliq As Double() = Nothing

            Dim alreadymt As Boolean = False

            If Calculator.EnableParallelProcessing Then

                Dim task1 = Task.Factory.StartNew(Sub()
                                                      fugliq = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                                                  End Sub,
                                                        Calculator.TaskCancellationTokenSource.Token,
                                                        TaskCreationOptions.None,
                                                        Calculator.AppTaskScheduler)
                Dim task2 = Task.Factory.StartNew(Sub()
                                                      If type = "LV" Then
                                                          fugvap = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)
                                                      Else ' LL
                                                          fugvap = Me.DW_CalcFugCoeff(Vy, T, P, State.Liquid)
                                                      End If
                                                  End Sub,
                                                    Calculator.TaskCancellationTokenSource.Token,
                                                    TaskCreationOptions.None,
                                                    Calculator.AppTaskScheduler)
                Task.WaitAll(task1, task2)

            Else
                fugliq = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                If type = "LV" Then
                    fugvap = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)
                Else ' LL
                    fugvap = Me.DW_CalcFugCoeff(Vy, T, P, State.Liquid)
                End If
            End If

            Dim n As Integer = UBound(fugvap)
            Dim i As Integer
            Dim K(n) As Double

            K = fugliq.DivideY(fugvap)

            Dim cprops As List(Of Interfaces.ICompoundConstantProperties) = Nothing

            If cprops Is Nothing Then cprops = DW_GetConstantProperties()

            If Calculator.EnableParallelProcessing Then
                Parallel.For(0, n + 1, Sub(ii)
                                           Dim Pc, Tc, w As Double
                                           If K(ii) = 0.0# Or Double.IsInfinity(K(ii)) Or Double.IsNaN(K(ii)) Then
                                               Pc = cprops(ii).Critical_Pressure
                                               Tc = cprops(ii).Critical_Temperature
                                               w = cprops(ii).Acentric_Factor
                                               If type = "LV" Then
                                                   K(ii) = Pc / P * Math.Exp(5.373 * (1 + w) * (1 - Tc / T))
                                               Else
                                                   K(ii) = 1.0#
                                               End If
                                           End If
                                       End Sub)
            Else
                Dim Pc, Tc, w As Double
                For i = 0 To n
                    If K(i) = 0.0# Or Double.IsInfinity(K(i)) Or Double.IsNaN(K(i)) Then
                        Pc = cprops(i).Critical_Pressure
                        Tc = cprops(i).Critical_Temperature
                        w = cprops(i).Acentric_Factor
                        If type = "LV" Then
                            K(i) = Pc / P * Math.Exp(5.373 * (1 + w) * (1 - Tc / T))
                        Else
                            K(i) = 1.0#
                        End If
                    End If
                Next
            End If


            If Me.AUX_CheckTrivial(K) Then
                Dim Pc, Tc, w As Double
                If Not Parameters.ContainsKey("PP_FLASHALGORITHMIDEALKFALLBACK") Then Parameters.Add("PP_FLASHALGORITHMIDEALKFALLBACK", 1)
                If Parameters("PP_FLASHALGORITHMIDEALKFALLBACK") = 1 Then
                    If cprops Is Nothing Then cprops = DW_GetConstantProperties()
                    For i = 0 To n
                        Pc = cprops(i).Critical_Pressure
                        Tc = cprops(i).Critical_Temperature
                        w = cprops(i).Acentric_Factor
                        If type = "LV" Then
                            K(i) = Pc / P * Math.Exp(5.373 * (1 + w) * (1 - Tc / T))
                        Else
                            K(i) = 1.0#
                        End If
                    Next
                End If
            End If

            Return K

        End Function

        ''' <summary>
        ''' Calculates K-values of components in a mixture.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the mixture.</param>
        ''' <param name="T">Temperature of the system, in K.</param>
        ''' <param name="P">Pressure of the system, in Pa.</param>
        ''' <returns>An array containing K-values for all components in the mixture.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public Overridable Overloads Function DW_CalcKvalue(ByVal Vx As System.Array, ByVal T As Double, ByVal P As Double) As Double()

            Dim i As Integer
            Dim result = Me.FlashBase.Flash_PT(Vx, P, T, Me)
            Dim n As Integer = UBound(Vx)
            Dim K(n) As Double

            i = 0
            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(1).Compounds.Values
                K(i) = (result(3)(i) / result(2)(i))
                i += 1
            Next

            i = 0
            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(1).Compounds.Values
                If K(i) = 0 Then K(i) = Me.AUX_PVAPi(subst.Name, T) / P
                If Double.IsInfinity(K(i)) Or Double.IsNaN(K(i)) Then
                    Dim Pc, Tc, w As Double
                    Pc = subst.ConstantProperties.Critical_Pressure
                    Tc = subst.ConstantProperties.Critical_Temperature
                    w = subst.ConstantProperties.Acentric_Factor
                    K(i) = Pc / P * Math.Exp(5.373 * (1 + w) * (1 - Tc / T))
                End If
                i += 1
            Next

            If Me.AUX_CheckTrivial(K) Then
                For i = 0 To UBound(Vx)
                    K(i) = Me.AUX_PVAPi(i, T) / P
                    i += 1
                Next
            End If

            Return K

        End Function

        ''' <summary>
        ''' Does a Bubble Pressure calculation for the specified liquid composition at the specified temperature.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing liquid phase molar composition for each component in the mixture.</param>
        ''' <param name="T">Temperature in K</param>
        ''' <param name="Pref">Initial estimate for Pressure, in Pa</param>
        ''' <param name="K">Vector with initial estimates for K-values</param>
        ''' <param name="ReuseK">Boolean indicating wether to use the initial estimates for K-values or not.</param>
        ''' <returns>Returns the object vector {L, V, Vx, Vy, P, ecount, Ki} where L is the liquid phase molar fraction, 
        ''' V is the vapor phase molar fraction, Vx is the liquid phase molar composition vector, Vy is the vapor phase molar 
        ''' composition vector, P is the calculated Pressure in Pa, ecount is the number of iterations and Ki is a vector containing 
        ''' the calculated K-values.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public Overridable Function DW_CalcBubP(ByVal Vx As System.Array, ByVal T As Double, Optional ByVal Pref As Double = 0, Optional ByVal K As System.Array = Nothing, Optional ByVal ReuseK As Boolean = False) As Object
            Return Me.FlashBase.Flash_TV(Vx, T, 0, Pref, Me, ReuseK, K)
        End Function

        ''' <summary>
        ''' Does a Bubble Temperature calculation for the specified liquid composition at the specified pressure.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing liquid phase molar composition for each component in the mixture.</param>
        ''' <param name="P"></param>
        ''' <param name="Tref"></param>
        ''' <param name="K">Vector with initial estimates for K-values</param>
        ''' <param name="ReuseK">Boolean indicating wether to use the initial estimates for K-values or not.</param>
        ''' <returns>Returns the object vector {L, V, Vx, Vy, T, ecount, Ki} where L is the liquid phase molar fraction, 
        ''' V is the vapor phase molar fraction, Vx is the liquid phase molar composition vector, Vy is the vapor phase molar 
        ''' composition vector, T is the calculated Temperature in K, ecount is the number of iterations and Ki is a vector containing 
        ''' the calculated K-values.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public Overridable Function DW_CalcBubT(ByVal Vx As System.Array, ByVal P As Double, Optional ByVal Tref As Double = 0, Optional ByVal K As System.Array = Nothing, Optional ByVal ReuseK As Boolean = False) As Object
            Return Me.FlashBase.Flash_PV(Vx, P, 0, Tref, Me, ReuseK, K)
        End Function

        ''' <summary>
        ''' Does a Dew Pressure calculation for the specified vapor phase composition at the specified temperature.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing vapor phase molar composition for each component in the mixture.</param>
        ''' <param name="T">Temperature in K</param>
        ''' <param name="Pref">Initial estimate for Pressure, in Pa</param>
        ''' <param name="K">Vector with initial estimates for K-values</param>
        ''' <param name="ReuseK">Boolean indicating wether to use the initial estimates for K-values or not.</param>
        ''' <returns>Returns the object vector {L, V, Vx, Vy, P, ecount, Ki} where L is the liquid phase molar fraction, 
        ''' V is the vapor phase molar fraction, Vx is the liquid phase molar composition vector, Vy is the vapor phase molar 
        ''' composition vector, P is the calculated Pressure in Pa, ecount is the number of iterations and Ki is a vector containing 
        ''' the calculated K-values.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public Overridable Function DW_CalcDewP(ByVal Vx As System.Array, ByVal T As Double, Optional ByVal Pref As Double = 0, Optional ByVal K As System.Array = Nothing, Optional ByVal ReuseK As Boolean = False) As Object
            Return Me.FlashBase.Flash_TV(Vx, T, 1, Pref, Me, ReuseK, K)
        End Function

        ''' <summary>
        ''' Does a Dew Temperature calculation for the specified vapor composition at the specified pressure.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing vapor phase molar composition for each component in the mixture.</param>
        ''' <param name="P"></param>
        ''' <param name="Tref"></param>
        ''' <param name="K">Vector with initial estimates for K-values</param>
        ''' <param name="ReuseK">Boolean indicating wether to use the initial estimates for K-values or not.</param>
        ''' <returns>Returns the object vector {L, V, Vx, Vy, T, ecount, Ki} where L is the liquid phase molar fraction, 
        ''' V is the vapor phase molar fraction, Vx is the liquid phase molar composition vector, Vy is the vapor phase molar 
        ''' composition vector, T is the calculated Temperature in K, ecount is the number of iterations and Ki is a vector containing 
        ''' the calculated K-values.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public Overridable Function DW_CalcDewT(ByVal Vx As System.Array, ByVal P As Double, Optional ByVal Tref As Double = 0, Optional ByVal K As System.Array = Nothing, Optional ByVal ReuseK As Boolean = False) As Object
            Return Me.FlashBase.Flash_PV(Vx, P, 1, Tref, Me, ReuseK, K)
        End Function

        ''' <summary>
        ''' Calculates fugacity coefficients for the specified composition at the specified conditions.
        ''' </summary>
        ''' <param name="Vx">Vector of doubles containing the molar composition of the mixture.</param>
        ''' <param name="T">Temperature in K</param>
        ''' <param name="P">Pressure in Pa</param>
        ''' <param name="st">Mixture state (Liquid or Vapor)</param>
        ''' <returns>A vector of doubles containing fugacity coefficients for the components in the mixture.</returns>
        ''' <remarks>The composition vector must follow the same sequence as the components which were added in the material stream.</remarks>
        Public MustOverride Function DW_CalcFugCoeff(ByVal Vx As Array, ByVal T As Double, ByVal P As Double, ByVal st As State) As Double()

        Public MustOverride Function SupportsComponent(ByVal comp As Interfaces.ICompoundConstantProperties) As Boolean

        Public MustOverride Sub DW_CalcPhaseProps(ByVal Phase As Phase)

        Public Overridable Sub DW_CalcTwoPhaseProps(ByVal Phase1 As PropertyPackages.Phase, ByVal Phase2 As PropertyPackages.Phase)

            Dim T As Double

            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.surfaceTension = Me.AUX_SURFTM(T)

        End Sub

        Public Function DW_CalcGibbsEnergy(ByVal Vx As System.Array, ByVal T As Double, ByVal P As Double, Optional ByVal forcephase As String = "") As Double

            Dim fugvap As Object = Nothing
            Dim fugliq As Object = Nothing

            If Calculator.EnableParallelProcessing Then

                Dim task1 As Task = New Task(Sub()
                                                 fugliq = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                                             End Sub)
                Dim task2 As Task = New Task(Sub()
                                                 fugvap = Me.DW_CalcFugCoeff(Vx, T, P, State.Vapor)
                                             End Sub)
                task1.Start()
                task2.Start()
                Task.WaitAll(task1, task2)

            Else
                fugliq = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                fugvap = Me.DW_CalcFugCoeff(Vx, T, P, State.Vapor)
            End If

            Dim n As Integer = UBound(Vx)
            Dim i As Integer

            Dim g, gid, gexv, gexl As Double

            gid = 0.0#

            gexv = 0.0#
            gexl = 0.0#
            For i = 0 To n
                If Vx(i) <> 0.0# Then gexv += Vx(i) * Log(fugvap(i)) * 8.314 * T
                If Vx(i) <> 0.0# Then gexl += Vx(i) * Log(fugliq(i)) * 8.314 * T
            Next

            If forcephase = "L" Then
                g = gid + gexl
            ElseIf forcephase = "V" Then
                g = gid + gexv
            Else
                If gexv < gexl Then g = gid + gexv Else g = gid + gexl
            End If

            Return g 'kJ/kmol

        End Function

        Public Overridable Sub DW_CalcOverallProps()

            Dim HL, HV, HS, SL, SV, SS, DL, DV, DS, CPL, CPV, CPS, KL, KV, KS, CVL, CVV, CSV As Nullable(Of Double)
            Dim xl, xv, xs, wl, wv, ws, vl, vv, vs, result As Double

            xl = Me.CurrentMaterialStream.Phases(1).Properties.molarfraction.GetValueOrDefault
            xv = Me.CurrentMaterialStream.Phases(2).Properties.molarfraction.GetValueOrDefault
            xs = Me.CurrentMaterialStream.Phases(7).Properties.molarfraction.GetValueOrDefault

            wl = Me.CurrentMaterialStream.Phases(1).Properties.massfraction.GetValueOrDefault
            wv = Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault
            ws = Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault

            DL = Me.CurrentMaterialStream.Phases(1).Properties.density.GetValueOrDefault
            DV = Me.CurrentMaterialStream.Phases(2).Properties.density.GetValueOrDefault
            DS = Me.CurrentMaterialStream.Phases(7).Properties.density.GetValueOrDefault

            If Double.IsNaN(DL) Then DL = 0.0#

            Dim tl As Double = 0.0#
            Dim tv As Double = 0.0#
            Dim ts As Double = 0.0#

            If DL <> 0.0# And Not Double.IsNaN(DL) Then tl = Me.CurrentMaterialStream.Phases(1).Properties.massfraction.GetValueOrDefault / DL.GetValueOrDefault
            If DV <> 0.0# And Not Double.IsNaN(DV) Then tv = Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault / DV.GetValueOrDefault
            If DS <> 0.0# And Not Double.IsNaN(DS) Then ts = Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault / DS.GetValueOrDefault

            vl = tl / (tl + tv + ts)
            vv = tv / (tl + tv + ts)
            vs = ts / (tl + tv + ts)

            If xl = 1 Then
                vl = 1
                vv = 0
                vs = 0
            ElseIf xv = 1 Then
                vl = 0
                vv = 1
                vs = 0
            ElseIf xs = 1 Then
                vl = 0
                vv = 0
                vs = 1
            End If

            result = vl * DL.GetValueOrDefault + vv * DV.GetValueOrDefault + vs * DS.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.density = result

            HL = Me.CurrentMaterialStream.Phases(1).Properties.enthalpy.GetValueOrDefault
            HV = Me.CurrentMaterialStream.Phases(2).Properties.enthalpy.GetValueOrDefault
            HS = Me.CurrentMaterialStream.Phases(7).Properties.enthalpy.GetValueOrDefault

            result = wl * HL.GetValueOrDefault + wv * HV.GetValueOrDefault + ws * HS.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.enthalpy = result

            SL = Me.CurrentMaterialStream.Phases(1).Properties.entropy.GetValueOrDefault
            SV = Me.CurrentMaterialStream.Phases(2).Properties.entropy.GetValueOrDefault
            SS = Me.CurrentMaterialStream.Phases(7).Properties.entropy.GetValueOrDefault

            result = wl * SL.GetValueOrDefault + wv * SV.GetValueOrDefault + ws * SS.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.entropy = result

            Me.CurrentMaterialStream.Phases(0).Properties.compressibilityFactor = Nothing

            CPL = Me.CurrentMaterialStream.Phases(1).Properties.heatCapacityCp.GetValueOrDefault
            CPV = Me.CurrentMaterialStream.Phases(2).Properties.heatCapacityCp.GetValueOrDefault
            CPS = Me.CurrentMaterialStream.Phases(7).Properties.heatCapacityCp.GetValueOrDefault

            result = wl * CPL.GetValueOrDefault + wv * CPV.GetValueOrDefault + ws * CPS.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.heatCapacityCp = result

            CVL = Me.CurrentMaterialStream.Phases(1).Properties.heatCapacityCv.GetValueOrDefault
            CVV = Me.CurrentMaterialStream.Phases(2).Properties.heatCapacityCv.GetValueOrDefault
            CSV = Me.CurrentMaterialStream.Phases(7).Properties.heatCapacityCv.GetValueOrDefault

            result = wl * CVL.GetValueOrDefault + wv * CVV.GetValueOrDefault + ws * CSV.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.heatCapacityCv = result

            result = Me.AUX_MMM(Phase.Mixture)
            Me.CurrentMaterialStream.Phases(0).Properties.molecularWeight = result

            result = Me.CurrentMaterialStream.Phases(0).Properties.enthalpy.GetValueOrDefault * Me.CurrentMaterialStream.Phases(0).Properties.molecularWeight.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.molar_enthalpy = result
            result = Me.CurrentMaterialStream.Phases(0).Properties.entropy.GetValueOrDefault * Me.CurrentMaterialStream.Phases(0).Properties.molecularWeight.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.molar_entropy = result

            KL = Me.CurrentMaterialStream.Phases(1).Properties.thermalConductivity.GetValueOrDefault
            KV = Me.CurrentMaterialStream.Phases(2).Properties.thermalConductivity.GetValueOrDefault
            KS = Me.CurrentMaterialStream.Phases(7).Properties.thermalConductivity.GetValueOrDefault

            result = xl * KL.GetValueOrDefault + xv * KV.GetValueOrDefault + xs * KS.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.thermalConductivity = result

            Me.CurrentMaterialStream.Phases(0).Properties.viscosity = Nothing

            Me.CurrentMaterialStream.Phases(0).Properties.kinematic_viscosity = Nothing

            Dim P As Double = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault
            Dim T As Double = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault

            If Not Calculator.CAPEOPENMode Then
                If Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.CalculateBubbleAndDewPoints _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then
                    Try
                        Dim Vz As Double() = Me.RET_VMOL(Phase.Mixture)
                        Dim myres As Object = Me.FlashBase.Flash_PV(Vz, P, 0, 0, Me)
                        'check if liquid phase is stable.
                        Dim myres2 As Object = Me.FlashBase.Flash_PT(Vz, P, myres(4), Me)
                        If myres2(5) > 0.0# Then
                            If Abs(myres2(2)(0) - myres2(6)(0)) > 0.01 Then
                                Me.CurrentMaterialStream.Phases(0).Properties.bubbleTemperature = Me.FlashBase.BubbleTemperature_LLE(Vz, myres2(2), myres2(6), P, myres(4) - 40, myres(4) + 40, Me)
                            End If
                        Else
                            Me.CurrentMaterialStream.Phases(0).Properties.bubbleTemperature = myres(4)
                        End If
                    Catch ex As Exception
                        Me.CurrentMaterialStream.Flowsheet.ShowMessage("Bubble Temperature calculation error: " & ex.Message.ToString, Interfaces.IFlowsheet.MessageType.GeneralError)
                    End Try
                    Try
                        result = Me.DW_CalcEquilibrio_ISOL(FlashSpec.P, FlashSpec.VAP, P, 1, 0)(2)
                        Me.CurrentMaterialStream.Phases(0).Properties.dewTemperature = result
                    Catch ex As Exception
                        Me.CurrentMaterialStream.Flowsheet.ShowMessage("Dew Temperature calculation error: " & ex.Message.ToString, Interfaces.IFlowsheet.MessageType.GeneralError)
                    End Try
                    Try
                        Dim Vz As Double() = Me.RET_VMOL(Phase.Mixture)
                        Dim myres As Object = Me.FlashBase.Flash_TV(Vz, T, 0, 0, Me)
                        'check if liquid phase is stable.
                        Dim myres2 As Object = Me.FlashBase.Flash_PT(Vz, myres(4), T, Me)
                        If myres2(5) > 0.0# Then
                            If Abs(myres2(2)(0) - myres2(6)(0)) > 0.01 Then
                                Me.CurrentMaterialStream.Phases(0).Properties.bubblePressure = Me.FlashBase.BubblePressure_LLE(Vz, myres2(2), myres2(6), myres(4), T, Me)
                            End If
                        Else
                            Me.CurrentMaterialStream.Phases(0).Properties.bubblePressure = myres(4)
                        End If
                    Catch ex As Exception
                        Me.CurrentMaterialStream.Flowsheet.ShowMessage("Bubble Pressure calculation error: " & ex.Message.ToString, Interfaces.IFlowsheet.MessageType.GeneralError)
                    End Try
                    Try
                        result = Me.DW_CalcEquilibrio_ISOL(FlashSpec.T, FlashSpec.VAP, T, 1, 0)(3)
                        Me.CurrentMaterialStream.Phases(0).Properties.dewPressure = result
                    Catch ex As Exception
                        Me.CurrentMaterialStream.Flowsheet.ShowMessage("Dew Pressure calculation error: " & ex.Message.ToString, Interfaces.IFlowsheet.MessageType.GeneralError)
                    End Try
                End If
            End If

        End Sub

        Public Overridable Sub DW_CalcOverallDensity()

            Dim DL1, DL2, DL3, DL4, DV, DS As Nullable(Of Double)
            Dim xl1, xl2, xl3, xl4, xv, xs, wl1, wl2, wl3, wl4, wv, ws, vl1, vl2, vl3, vl4, vv, vs, result As Double

            xl1 = Me.CurrentMaterialStream.Phases(3).Properties.molarfraction.GetValueOrDefault
            xl2 = Me.CurrentMaterialStream.Phases(4).Properties.molarfraction.GetValueOrDefault
            xl3 = Me.CurrentMaterialStream.Phases(5).Properties.molarfraction.GetValueOrDefault
            xl4 = Me.CurrentMaterialStream.Phases(6).Properties.molarfraction.GetValueOrDefault
            xv = Me.CurrentMaterialStream.Phases(2).Properties.molarfraction.GetValueOrDefault
            xs = Me.CurrentMaterialStream.Phases(7).Properties.molarfraction.GetValueOrDefault

            wl1 = Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault
            wl2 = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault
            wl3 = Me.CurrentMaterialStream.Phases(5).Properties.massfraction.GetValueOrDefault
            wl4 = Me.CurrentMaterialStream.Phases(6).Properties.massfraction.GetValueOrDefault
            wv = Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault
            ws = Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault

            Me.DW_CalcProp("density", Phase.Liquid1)
            Me.DW_CalcProp("density", Phase.Liquid2)
            Me.DW_CalcProp("density", Phase.Liquid3)
            Me.DW_CalcProp("density", Phase.Aqueous)
            Me.DW_CalcProp("density", Phase.Vapor)

            DL1 = Me.CurrentMaterialStream.Phases(3).Properties.density.GetValueOrDefault
            DL2 = Me.CurrentMaterialStream.Phases(4).Properties.density.GetValueOrDefault
            DL3 = Me.CurrentMaterialStream.Phases(5).Properties.density.GetValueOrDefault
            DL4 = Me.CurrentMaterialStream.Phases(6).Properties.density.GetValueOrDefault
            DV = Me.CurrentMaterialStream.Phases(2).Properties.density.GetValueOrDefault
            DS = Me.AUX_SOLIDDENS()

            If Double.IsNaN(DL1) Or Double.IsInfinity(DL1) Then DL1 = 0.0#
            If Double.IsNaN(DL2) Or Double.IsInfinity(DL2) Then DL2 = 0.0#
            If Double.IsNaN(DL3) Or Double.IsInfinity(DL3) Then DL3 = 0.0#
            If Double.IsNaN(DL4) Or Double.IsInfinity(DL4) Then DL4 = 0.0#
            If Double.IsNaN(DV) Or Double.IsInfinity(DV) Then DV = 0.0#
            If Double.IsNaN(DS) Or Double.IsInfinity(DS) Then DS = 0.0#

            Dim tl1 As Double = 0.0#
            Dim tl2 As Double = 0.0#
            Dim tl3 As Double = 0.0#
            Dim tl4 As Double = 0.0#
            Dim tv As Double = 0.0#
            Dim ts As Double = 0.0#

            If DL1 <> 0.0# And Not Double.IsNaN(DL1) Then tl1 = Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault / DL1.GetValueOrDefault
            If DL2 <> 0.0# And Not Double.IsNaN(DL2) Then tl2 = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault / DL2.GetValueOrDefault
            If DL3 <> 0.0# And Not Double.IsNaN(DL3) Then tl3 = Me.CurrentMaterialStream.Phases(5).Properties.massfraction.GetValueOrDefault / DL3.GetValueOrDefault
            If DL4 <> 0.0# And Not Double.IsNaN(DL4) Then tl4 = Me.CurrentMaterialStream.Phases(6).Properties.massfraction.GetValueOrDefault / DL4.GetValueOrDefault
            If DV <> 0.0# And Not Double.IsNaN(DV) Then tv = Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault / DV.GetValueOrDefault
            If DS <> 0.0# And Not Double.IsNaN(DS) Then ts = Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault / DS.GetValueOrDefault

            vl1 = tl1 / (tl1 + tl2 + tl3 + tl4 + tv + ts)
            vl2 = tl2 / (tl1 + tl2 + tl3 + tl4 + tv + ts)
            vl3 = tl3 / (tl1 + tl2 + tl3 + tl4 + tv + ts)
            vl4 = tl4 / (tl1 + tl2 + tl3 + tl4 + tv + ts)
            vv = tv / (tl1 + tl2 + tl3 + tl4 + tv + ts)
            vs = ts / (tl1 + tl2 + tl3 + tl4 + tv + ts)

            If xl1 = 1 Then
                vl1 = 1
                vl2 = 0
                vl3 = 0
                vl4 = 0
                vv = 0
                vs = 0
            ElseIf xl2 = 1 Then
                vl1 = 0
                vl2 = 1
                vl3 = 0
                vl4 = 0
                vv = 1
                vs = 0
            ElseIf xl3 = 1 Then
                vl1 = 0
                vl2 = 0
                vl3 = 1
                vl4 = 0
                vv = 1
                vs = 0
            ElseIf xl4 = 1 Then
                vl1 = 0
                vl2 = 0
                vl3 = 0
                vl4 = 1
                vv = 1
                vs = 0
            ElseIf xv = 1 Then
                vl1 = 0
                vl2 = 0
                vl3 = 0
                vl4 = 0
                vv = 1
                vs = 0
            ElseIf xs = 1 Then
                vl1 = 0
                vl2 = 0
                vl3 = 0
                vl4 = 0
                vv = 0
                vs = 1
            End If

            result = vl1 * DL1.GetValueOrDefault + vl2 * DL2.GetValueOrDefault + vl3 * DL3.GetValueOrDefault + vl4 * DL4.GetValueOrDefault + vv * DV.GetValueOrDefault + vs * DS.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(0).Properties.density = result

        End Sub

        Public Overridable Sub DW_CalcLiqMixtureProps()

            Dim hl, hl1, hl2, hl3, hw, sl, sl1, sl2, sl3, sw, dl, dl1, dl2, dl3, dw As Double
            Dim cpl, cpl1, cpl2, cpl3, cpw, cvl, cvl1, cvl2, cvl3, cvw As Double
            Dim kl, kl1, kl2, kl3, kw, vil, vil1, vil2, vil3, viw As Double
            Dim xl, xl1, xl2, xl3, xw, wl, wl1, wl2, wl3, ww As Double
            Dim xlf, xlf1, xlf2, xlf3, xwf, wlf, wlf1, wlf2, wlf3, wwf As Double
            Dim cml, cml1, cml2, cml3, cmw, cwl, cwl1, cwl2, cwl3, cww As Double

            xl1 = Me.CurrentMaterialStream.Phases(3).Properties.molarfraction.GetValueOrDefault
            xl2 = Me.CurrentMaterialStream.Phases(4).Properties.molarfraction.GetValueOrDefault
            xl3 = Me.CurrentMaterialStream.Phases(5).Properties.molarfraction.GetValueOrDefault
            xw = Me.CurrentMaterialStream.Phases(6).Properties.molarfraction.GetValueOrDefault

            If Not xl1.IsValid Then xl1 = 0.0#
            If Not xl2.IsValid Then xl2 = 0.0#
            If Not xl3.IsValid Then xl3 = 0.0#
            If Not xw.IsValid Then xw = 0.0#

            xl = xl1 + xl2 + xl3 + xw
            Me.CurrentMaterialStream.Phases(1).Properties.molarfraction = xl

            wl1 = Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault
            wl2 = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault
            wl3 = Me.CurrentMaterialStream.Phases(5).Properties.massfraction.GetValueOrDefault
            ww = Me.CurrentMaterialStream.Phases(6).Properties.massfraction.GetValueOrDefault

            If Not wl1.IsValid Then wl1 = 0.0#
            If Not wl2.IsValid Then wl2 = 0.0#
            If Not wl3.IsValid Then wl3 = 0.0#
            If Not ww.IsValid Then ww = 0.0#

            wl = wl1 + wl2 + wl3 + ww
            Me.CurrentMaterialStream.Phases(1).Properties.massfraction = wl

            xlf1 = Me.CurrentMaterialStream.Phases(3).Properties.molarflow.GetValueOrDefault
            xlf2 = Me.CurrentMaterialStream.Phases(4).Properties.molarflow.GetValueOrDefault
            xlf3 = Me.CurrentMaterialStream.Phases(5).Properties.molarflow.GetValueOrDefault
            xwf = Me.CurrentMaterialStream.Phases(6).Properties.molarflow.GetValueOrDefault

            If Not xlf1.IsValid Then xlf1 = 0.0#
            If Not xlf2.IsValid Then xlf2 = 0.0#
            If Not xlf3.IsValid Then xlf3 = 0.0#
            If Not xwf.IsValid Then xwf = 0.0#

            xlf = xlf1 + xlf2 + xlf3 + xwf
            Me.CurrentMaterialStream.Phases(1).Properties.molarflow = xlf

            wlf1 = Me.CurrentMaterialStream.Phases(3).Properties.massflow.GetValueOrDefault
            wlf2 = Me.CurrentMaterialStream.Phases(4).Properties.massflow.GetValueOrDefault
            wlf3 = Me.CurrentMaterialStream.Phases(5).Properties.massflow.GetValueOrDefault
            wwf = Me.CurrentMaterialStream.Phases(6).Properties.massflow.GetValueOrDefault

            If Not wlf1.IsValid Then wlf1 = 0.0#
            If Not wlf2.IsValid Then wlf2 = 0.0#
            If Not wlf3.IsValid Then wlf3 = 0.0#
            If Not wwf.IsValid Then wwf = 0.0#

            wlf = wlf1 + wlf2 + wlf3 + wwf
            Me.CurrentMaterialStream.Phases(1).Properties.massflow = wlf

            For Each c As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(1).Compounds.Values

                cml1 = Me.CurrentMaterialStream.Phases(3).Compounds(c.Name).MoleFraction.GetValueOrDefault
                cml2 = Me.CurrentMaterialStream.Phases(4).Compounds(c.Name).MoleFraction.GetValueOrDefault
                cml3 = Me.CurrentMaterialStream.Phases(5).Compounds(c.Name).MoleFraction.GetValueOrDefault
                cmw = Me.CurrentMaterialStream.Phases(6).Compounds(c.Name).MoleFraction.GetValueOrDefault

                If Not cml1.IsValid Then cml1 = 0.0#
                If Not cml2.IsValid Then cml2 = 0.0#
                If Not cml3.IsValid Then cml3 = 0.0#
                If Not cmw.IsValid Then cmw = 0.0#

                cwl1 = Me.CurrentMaterialStream.Phases(3).Compounds(c.Name).MassFraction.GetValueOrDefault
                cwl2 = Me.CurrentMaterialStream.Phases(4).Compounds(c.Name).MassFraction.GetValueOrDefault
                cwl3 = Me.CurrentMaterialStream.Phases(5).Compounds(c.Name).MassFraction.GetValueOrDefault
                cww = Me.CurrentMaterialStream.Phases(6).Compounds(c.Name).MassFraction.GetValueOrDefault

                If Not cwl1.IsValid Then cwl1 = 0.0#
                If Not cwl2.IsValid Then cwl2 = 0.0#
                If Not cwl3.IsValid Then cwl3 = 0.0#
                If Not cww.IsValid Then cww = 0.0#

                cml = (xl1 * cml1 + xl2 * cml2 + xl3 * cml3 + xw * cmw) / xl
                cwl = (wl1 * cwl1 + wl2 * cwl2 + wl3 * cwl3 + ww * cww) / wl
                c.MoleFraction = cml
                c.MassFraction = cwl

            Next

            If wl1 > 0 Then dl1 = Me.CurrentMaterialStream.Phases(3).Properties.density.GetValueOrDefault Else dl1 = 1
            If wl2 > 0 Then dl2 = Me.CurrentMaterialStream.Phases(4).Properties.density.GetValueOrDefault Else dl2 = 1
            If wl3 > 0 Then dl3 = Me.CurrentMaterialStream.Phases(5).Properties.density.GetValueOrDefault Else dl3 = 1
            If ww > 0 Then dw = Me.CurrentMaterialStream.Phases(6).Properties.density.GetValueOrDefault Else dw = 1

            dl = wl1 / dl1 + wl2 / dl2 + wl3 / dl3 + ww / dw
            dl = wl / dl
            Me.CurrentMaterialStream.Phases(1).Properties.density = dl

            If Double.IsNaN(wlf / dl) Then
                Me.CurrentMaterialStream.Phases(1).Properties.volumetric_flow = 0.0#
            Else
                Me.CurrentMaterialStream.Phases(1).Properties.volumetric_flow = wlf / dl
            End If

            If wl = 0 Then wl = 1.0#

            hl1 = Me.CurrentMaterialStream.Phases(3).Properties.enthalpy.GetValueOrDefault
            hl2 = Me.CurrentMaterialStream.Phases(4).Properties.enthalpy.GetValueOrDefault
            hl3 = Me.CurrentMaterialStream.Phases(5).Properties.enthalpy.GetValueOrDefault
            hw = Me.CurrentMaterialStream.Phases(6).Properties.enthalpy.GetValueOrDefault

            hl = hl1 * wl1 + hl2 * wl2 + hl3 * wl3 + hw * ww

            Me.CurrentMaterialStream.Phases(1).Properties.enthalpy = hl / wl

            sl1 = Me.CurrentMaterialStream.Phases(3).Properties.entropy.GetValueOrDefault
            sl2 = Me.CurrentMaterialStream.Phases(4).Properties.entropy.GetValueOrDefault
            sl3 = Me.CurrentMaterialStream.Phases(5).Properties.entropy.GetValueOrDefault
            sw = Me.CurrentMaterialStream.Phases(6).Properties.entropy.GetValueOrDefault

            sl = sl1 * wl1 + sl2 * wl2 + sl3 * wl3 + sw * ww

            Me.CurrentMaterialStream.Phases(1).Properties.entropy = sl / wl

            cpl1 = Me.CurrentMaterialStream.Phases(3).Properties.heatCapacityCp.GetValueOrDefault
            cpl2 = Me.CurrentMaterialStream.Phases(4).Properties.heatCapacityCp.GetValueOrDefault
            cpl3 = Me.CurrentMaterialStream.Phases(5).Properties.heatCapacityCp.GetValueOrDefault
            cpw = Me.CurrentMaterialStream.Phases(6).Properties.heatCapacityCp.GetValueOrDefault

            cpl = cpl1 * wl1 + cpl2 * wl2 + cpl3 * wl3 + cpw * ww

            Me.CurrentMaterialStream.Phases(1).Properties.heatCapacityCp = cpl / wl

            cvl1 = Me.CurrentMaterialStream.Phases(3).Properties.heatCapacityCv.GetValueOrDefault
            cvl2 = Me.CurrentMaterialStream.Phases(4).Properties.heatCapacityCv.GetValueOrDefault
            cvl3 = Me.CurrentMaterialStream.Phases(5).Properties.heatCapacityCv.GetValueOrDefault
            cvw = Me.CurrentMaterialStream.Phases(6).Properties.heatCapacityCv.GetValueOrDefault

            cvl = cvl1 * wl1 + cvl2 * wl2 + cvl3 * wl3 + cvw * ww

            Me.CurrentMaterialStream.Phases(1).Properties.heatCapacityCv = cvl / wl

            Dim result As Double

            result = Me.AUX_MMM(Phase.Liquid)
            Me.CurrentMaterialStream.Phases(1).Properties.molecularWeight = result

            result = Me.CurrentMaterialStream.Phases(1).Properties.enthalpy.GetValueOrDefault * Me.CurrentMaterialStream.Phases(1).Properties.molecularWeight.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(1).Properties.molar_enthalpy = result

            result = Me.CurrentMaterialStream.Phases(1).Properties.entropy.GetValueOrDefault * Me.CurrentMaterialStream.Phases(1).Properties.molecularWeight.GetValueOrDefault
            Me.CurrentMaterialStream.Phases(1).Properties.molar_entropy = result

            kl1 = Me.CurrentMaterialStream.Phases(3).Properties.thermalConductivity.GetValueOrDefault
            kl2 = Me.CurrentMaterialStream.Phases(4).Properties.thermalConductivity.GetValueOrDefault
            kl3 = Me.CurrentMaterialStream.Phases(5).Properties.thermalConductivity.GetValueOrDefault
            kw = Me.CurrentMaterialStream.Phases(6).Properties.thermalConductivity.GetValueOrDefault

            kl = kl1 * wl1 + kl2 * wl2 + kl3 * kl3 + kw * ww

            Me.CurrentMaterialStream.Phases(1).Properties.thermalConductivity = kl / wl

            vil1 = Me.CurrentMaterialStream.Phases(3).Properties.viscosity.GetValueOrDefault
            vil2 = Me.CurrentMaterialStream.Phases(4).Properties.viscosity.GetValueOrDefault
            vil3 = Me.CurrentMaterialStream.Phases(5).Properties.viscosity.GetValueOrDefault
            viw = Me.CurrentMaterialStream.Phases(6).Properties.viscosity.GetValueOrDefault

            vil = vil1 * wl1 + vil2 * vil2 + kl3 * vil3 + viw * ww

            Me.CurrentMaterialStream.Phases(1).Properties.viscosity = vil / wl

            Me.CurrentMaterialStream.Phases(1).Properties.kinematic_viscosity = vil / dl

            Me.CurrentMaterialStream.Phases(1).Properties.compressibilityFactor = 0.0#

        End Sub

        Public Function CalculateEquilibrium(calctype As Interfaces.Enums.FlashCalculationType, val1 As Double, val2 As Double, mixmolefrac() As Double, initialKval() As Double, initialestimate As Double) As Interfaces.IFlashCalculationResult Implements Interfaces.IPropertyPackage.CalculateEquilibrium

            Select Case calctype
                Case Interfaces.Enums.FlashCalculationType.PressureTemperature
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.T, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.PressureEnthalpy
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.H, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.PressureEntropy
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.S, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.PressureSolidFraction
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.SF, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.PressureVaporFraction
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.VAP, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.TemperatureEnthalpy
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.H, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.TemperatureEntropy
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.S, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.TemperatureSolidFraction
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.SF, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Interfaces.Enums.FlashCalculationType.TemperatureVaporFraction
                    Return Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.VAP, val1, val2, Me, mixmolefrac, initialKval, initialestimate)
                Case Else
                    Throw New NotImplementedException
            End Select

        End Function

        Public Function CalculateEquilibrium2(calctype As Enums.FlashCalculationType, val1 As Double, val2 As Double, initialestimate As Double) As IFlashCalculationResult Implements IPropertyPackage.CalculateEquilibrium2
            Return CalculateEquilibrium(calctype, val1, val2, RET_VMOL(Phase.Mixture), Nothing, initialestimate)
        End Function

        Public Overridable Sub DW_CalcEquilibrium(ByVal spec1 As FlashSpec, ByVal spec2 As FlashSpec)

            Me.CurrentMaterialStream.AtEquilibrium = False

            If Not Calculator.CAPEOPENMode Then
                Try
                    Me._tpseverity = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ThreePhaseFlashStabTestSeverity
                    Me._tpcompids = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ThreePhaseFlashStabTestCompIds
                Catch ex As Exception
                    Me._tpseverity = 0
                    Me._tpcompids = New String() {}
                Finally
                End Try
            End If

            Dim P, T, H, S, xv, xl, xl2, xs As Double
            Dim result As Object = Nothing
            Dim subst As Interfaces.ICompound
            Dim n As Integer = Me.CurrentMaterialStream.Phases(0).Compounds.Count
            Dim i As Integer = 0

            'for TVF/PVF/PH/PS flashes
            xv = Me.CurrentMaterialStream.Phases(2).Properties.molarfraction.GetValueOrDefault
            H = Me.CurrentMaterialStream.Phases(0).Properties.enthalpy.GetValueOrDefault
            S = Me.CurrentMaterialStream.Phases(0).Properties.entropy.GetValueOrDefault

            Me.DW_ZerarPhaseProps(Phase.Vapor)
            Me.DW_ZerarPhaseProps(Phase.Liquid)
            Me.DW_ZerarPhaseProps(Phase.Liquid1)
            Me.DW_ZerarPhaseProps(Phase.Liquid2)
            Me.DW_ZerarPhaseProps(Phase.Liquid3)
            Me.DW_ZerarPhaseProps(Phase.Aqueous)
            Me.DW_ZerarPhaseProps(Phase.Solid)
            Me.DW_ZerarComposicoes(Phase.Vapor)
            Me.DW_ZerarComposicoes(Phase.Liquid)
            Me.DW_ZerarComposicoes(Phase.Liquid1)
            Me.DW_ZerarComposicoes(Phase.Liquid2)
            Me.DW_ZerarComposicoes(Phase.Liquid3)
            Me.DW_ZerarComposicoes(Phase.Aqueous)
            Me.DW_ZerarComposicoes(Phase.Solid)

            Select Case spec1

                Case FlashSpec.T

                    Select Case spec2

                        Case FlashSpec.P

                            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

                            If Not T.IsValid Or Not P.IsValid Then Throw New ArgumentException("TP Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))
                            If Not T.IsPositive Or Not P.IsPositive Then Throw New ArgumentException("TP Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))

                            Dim ige As Double = 0
                            Dim fge As Double = 0
                            Dim dge As Double = 0

                            If Not Calculator.CAPEOPENMode Then
                                If Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ValidateEquilibriumCalc _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then

                                    ige = Me.DW_CalcGibbsEnergy(RET_VMOL(Phase.Mixture), T, P)

                                End If
                            End If

                            result = Me.FlashBase.Flash_PT(RET_VMOL(Phase.Mixture), P, T, Me)

                            'Dim result2 = Me.FlashBase.CalculateEquilibrium(FlashSpec.P, FlashSpec.T, P, T, Me, RET_VMOL(Phase.Mixture), Nothing, 0)

                            xl = result(0)
                            xv = result(1)
                            xl2 = result(5)
                            xs = result(7)

                            'xl = result2.GetLiquidPhase1MoleFraction
                            'xv = result2.GetVaporPhaseMoleFraction
                            'xl2 = result2.GetLiquidPhase2MoleFraction
                            'xs = result2.GetSolidPhaseMoleFraction

                            Dim Vx As Double() = result(2)
                            Dim Vy As Double() = result(3)
                            Dim Vx2 As Double() = result(6)
                            Dim Vs As Double() = result(8)

                            If Not Calculator.CAPEOPENMode Then

                                'identify phase
                                If Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.UsePhaseIdentificationAlgorithm Then
                                    If Me.ComponentName.Contains("SRK") Or Me.ComponentName.Contains("PR") Then
                                        If Not Me.AUX_IS_SINGLECOMP(Phase.Mixture) Then
                                            Dim newphase, eos As String
                                            If Me.ComponentName.Contains("SRK") Then eos = "SRK" Else eos = "PR"
                                            If xv = 1.0# Or xl = 1.0# Then
                                                If xv = 1.0# Then
                                                    newphase = Auxiliary.FlashAlgorithms.FlashAlgorithm.IdentifyPhase(Vy, P, T, Me, eos)
                                                    If newphase = "L" Then
                                                        xv = 0.0#
                                                        xl = 1.0#
                                                        Vx = Vy
                                                    End If
                                                Else
                                                    newphase = Auxiliary.FlashAlgorithms.FlashAlgorithm.IdentifyPhase(Vx, P, T, Me, eos)
                                                    If newphase = "V" Then
                                                        xv = 1.0#
                                                        xl = 0.0#
                                                        Vy = Vx
                                                    End If
                                                End If
                                            Else
                                                If xl2 = 0.0# Then
                                                    newphase = Auxiliary.FlashAlgorithms.FlashAlgorithm.IdentifyPhase(Vy, P, T, Me, eos)
                                                    If newphase = "L" Then
                                                        xl2 = xv
                                                        xv = 0.0#
                                                        Vx2 = Vy
                                                    End If
                                                    newphase = Auxiliary.FlashAlgorithms.FlashAlgorithm.IdentifyPhase(Vx, P, T, Me, eos)
                                                    If newphase = "V" Then
                                                        xv = 1.0#
                                                        xl = 0.0#
                                                        xl2 = 0.0#
                                                        Vy = RET_VMOL(Phase.Mixture)
                                                    End If
                                                ElseIf xv = 0.0# Then
                                                    newphase = Auxiliary.FlashAlgorithms.FlashAlgorithm.IdentifyPhase(Vx2, P, T, Me, eos)
                                                    If newphase = "V" Then
                                                        xv = xl2
                                                        xl2 = 0.0#
                                                        Vy = Vx2
                                                    End If
                                                End If
                                            End If
                                        End If
                                    End If
                                End If

                                If Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ValidateEquilibriumCalc _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then

                                    fge = xl * Me.DW_CalcGibbsEnergy(Vx, T, P)
                                    fge += xl2 * Me.DW_CalcGibbsEnergy(Vx2, T, P)
                                    fge += xv * Me.DW_CalcGibbsEnergy(Vy, T, P)

                                    dge = fge - ige

                                    Dim dgtol As Double = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.FlashValidationDGETolerancePct

                                    If dge > 0.0# And Math.Abs(dge / ige * 100) > Math.Abs(dgtol) Then
                                        Throw New Exception(Calculator.GetLocalString("InvalidFlashResult") & "(DGE = " & dge & " kJ/kg, " & Format(dge / ige * 100, "0.00") & "%)")
                                    End If

                                End If

                            End If

                            'do a density calculation check to order liquid phases from lighter to heavier

                            If xl2 <> 0.0# And xl = 0.0# Then
                                xl = result(5)
                                xl2 = 0.0#
                                Vx = result(6)
                                Vx2 = result(2)
                            ElseIf xl2 <> 0.0# And xl <> 0.0# Then
                                Dim dens1, dens2, xl0, xl20, Vx0(), Vx20() As Double
                                dens1 = Me.AUX_LIQDENS(T, Vx, P, 0, False)
                                dens2 = Me.AUX_LIQDENS(T, Vx2, P, 0, False)
                                If dens2 < dens1 Then
                                    xl0 = xl
                                    xl20 = xl2
                                    Vx0 = Vx
                                    Vx20 = Vx2
                                    xl = xl20
                                    xl2 = xl0
                                    Vx = Vx20
                                    Vx2 = Vx0
                                End If
                            End If

                            Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                            Me.CurrentMaterialStream.Phases(4).Properties.molarfraction = xl2
                            Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                            Me.CurrentMaterialStream.Phases(7).Properties.molarfraction = xs

                            Dim FCL = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                            Dim FCL2 = Me.DW_CalcFugCoeff(Vx2, T, P, State.Liquid)
                            Dim FCV = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)
                            Dim FCS = Me.DW_CalcFugCoeff(Vs, T, P, State.Solid)

                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                subst.MoleFraction = Vx(i)
                                subst.FugacityCoeff = FCL(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 3)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                subst.MoleFraction = Vx2(i)
                                subst.FugacityCoeff = FCL2(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 4)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                subst.MoleFraction = Vy(i)
                                subst.FugacityCoeff = FCV(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 2)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                subst.MoleFraction = Vs(i)
                                subst.FugacityCoeff = FCS(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 7)
                            Next

                            Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl * Me.AUX_MMM(Phase.Liquid1) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(4).Properties.massfraction = xl2 * Me.AUX_MMM(Phase.Liquid2) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv * Me.AUX_MMM(Phase.Vapor) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(7).Properties.massfraction = xs * Me.AUX_MMM(Phase.Solid) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))

                            Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                            For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                constprops.Add(su.ConstantProperties)
                            Next

                            Dim HM, HV, HL, HL2, HS As Double

                            If xl <> 0 Then HL = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid)
                            If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid)
                            If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor)
                            If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops)
                            HM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * HL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * HL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * HV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * HS

                            H = HM

                            Dim SM, SV, SL, SL2, SS As Double

                            If xl <> 0 Then SL = Me.DW_CalcEntropy(Vx, T, P, State.Liquid)
                            If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid)
                            If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor)
                            If xs <> 0 And T <> 298.15 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15)

                            SM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * SL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * SL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * SV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * SS

                            S = SM

                        Case FlashSpec.H

                            Throw New Exception(Calculator.GetLocalString("PropPack_FlashTHNotSupported"))

                        Case FlashSpec.S

                            Throw New Exception(Calculator.GetLocalString("PropPack_FlashTSNotSupported"))

                        Case FlashSpec.VAP

                            Dim KI(n) As Double
                            Dim HM, HV, HL, HL2, HS As Double
                            Dim SM, SV, SL, SL2, SS As Double

                            i = 0
                            Do
                                KI(i) = 0
                                i = i + 1
                            Loop Until i = n + 1

                            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

                            If Not T.IsValid Or Not xv.IsValid Then Throw New ArgumentException("TVF Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))
                            If Not T.IsPositive Or xv.IsNegative Then Throw New ArgumentException("TVF Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))

                            If Double.IsNaN(P) Or Double.IsInfinity(P) Then P = 0.0#

                            Dim Vx, Vx2, Vy, Vs As Double()

                            If Me.AUX_IS_SINGLECOMP(Phase.Mixture) Then

                                Dim Psat As Double
                                Dim vz As Object = Me.RET_VMOL(Phase.Mixture)

                                Psat = Me.AUX_PVAPM(T)

                                HL = Me.DW_CalcEnthalpy(vz, T, Psat, State.Liquid)
                                HV = Me.DW_CalcEnthalpy(vz, T, Psat, State.Vapor)
                                SL = Me.DW_CalcEntropy(vz, T, Psat, State.Liquid)
                                SV = Me.DW_CalcEntropy(vz, T, Psat, State.Vapor)
                                H = xv * HV + (1 - xv) * HL
                                S = xv * SV + (1 - xv) * SL
                                P = Psat
                                xl = 1 - xv
                                xl2 = 0.0#
                                xs = 0.0#

                                Vx = vz
                                Vy = vz
                                Vx2 = vz
                                Vs = vz

                            Else

                                result = Me.FlashBase.Flash_TV(RET_VMOL(Phase.Mixture), T, xv, P, Me)

                                P = result(4)

                                xl = result(0)
                                xv = result(1)
                                xl2 = result(7)
                                xs = result(9)

                                Vx = result(2)
                                Vy = result(3)
                                Vx2 = result(8)
                                Vs = result(10)

                            End If

                            Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                            Me.CurrentMaterialStream.Phases(4).Properties.molarfraction = xl2
                            Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                            Me.CurrentMaterialStream.Phases(7).Properties.molarfraction = xs

                            Dim FCL = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                            Dim FCL2 = Me.DW_CalcFugCoeff(Vx2, T, P, State.Liquid)
                            Dim FCV = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)

                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                subst.MoleFraction = Vx(i)
                                subst.FugacityCoeff = FCL(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 3)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                subst.MoleFraction = Vx2(i)
                                subst.FugacityCoeff = FCL2(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 4)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                subst.MoleFraction = Vy(i)
                                subst.FugacityCoeff = FCV(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 2)
                            Next

                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                subst.MoleFraction = Vs(i)
                                subst.FugacityCoeff = 1.0#
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 7)
                            Next

                            Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl * Me.AUX_MMM(Phase.Liquid1) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(4).Properties.massfraction = xl2 * Me.AUX_MMM(Phase.Liquid2) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv * Me.AUX_MMM(Phase.Vapor) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(7).Properties.massfraction = xs * Me.AUX_MMM(Phase.Solid) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))

                            Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                            For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                constprops.Add(su.ConstantProperties)
                            Next

                            If xl <> 0 Then HL = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid)
                            If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid)
                            If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor)
                            If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops)
                            HM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * HL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * HL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * HV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * HS

                            H = HM

                            If xl <> 0 Then SL = Me.DW_CalcEntropy(Vx, T, P, State.Liquid)
                            If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid)
                            If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor)
                            If xs <> 0 And T <> 298.15 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15)

                            SM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * SL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * SL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * SV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * SS

                            S = SM

                    End Select

                Case FlashSpec.P

                    Select Case spec2

                        Case FlashSpec.H

                            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

                            If Double.IsNaN(H) Or Double.IsInfinity(H) Then H = Me.CurrentMaterialStream.Phases(0).Properties.molar_enthalpy.GetValueOrDefault / Me.CurrentMaterialStream.Phases(0).Properties.molecularWeight.GetValueOrDefault

                            If Not T.IsValid Or Not H.IsValid Then Throw New ArgumentException("PH Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))
                            If Not T.IsPositive Then Throw New ArgumentException("PH Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))

                            If Me.AUX_IS_SINGLECOMP(Phase.Mixture) And Me.ComponentName <> "FPROPS" Then

                                Dim brentsolverT As New BrentOpt.Brent
                                brentsolverT.DefineFuncDelegate(AddressOf EnthalpyTx)

                                Dim hl, hv, sl, sv, Tsat As Double
                                Dim vz As Object = Me.RET_VMOL(Phase.Mixture)

                                Tsat = 0.0#
                                For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                    Tsat += subst.MoleFraction * Me.AUX_TSATi(P, subst.Name)
                                Next

                                hl = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Liquid)
                                hv = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Vapor)
                                sl = Me.DW_CalcEntropy(vz, Tsat, P, State.Liquid)
                                sv = Me.DW_CalcEntropy(vz, Tsat, P, State.Vapor)
                                If H <= hl Then
                                    xv = 0
                                    LoopVarState = State.Liquid
                                ElseIf H >= hv Then
                                    xv = 1
                                    LoopVarState = State.Vapor
                                Else
                                    xv = (H - hl) / (hv - hl)
                                End If
                                If Tsat > Me.AUX_TCM(Phase.Mixture) Then
                                    xv = 1.0#
                                    LoopVarState = State.Vapor
                                End If
                                xl = 1 - xv

                                If xv <> 0.0# And xv <> 1.0# Then
                                    T = Tsat
                                    S = xv * sv + (1 - xv) * sl
                                Else
                                    LoopVarF = H
                                    LoopVarX = P
                                    T = brentsolverT.BrentOpt(Me.AUX_TFM(Phase.Mixture), 2000, 20, 0.0001, 1000, Nothing)
                                    If xv = 0.0# Then
                                        S = Me.DW_CalcEntropy(vz, T, P, State.Liquid)
                                    Else
                                        S = Me.DW_CalcEntropy(vz, T, P, State.Vapor)
                                    End If
                                End If

                                If T <= Me.AUX_TFM(Phase.Mixture) Then

                                    'solid only.

                                    xv = 0.0#
                                    xl = 0.0#
                                    xs = 1.0#

                                    Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                                    For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                        constprops.Add(su.ConstantProperties)
                                    Next

                                    S = Me.DW_CalcSolidEnthalpy(T, vz, constprops) / (T - 298.15)

                                    Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                                    Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                                    Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl
                                    Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv
                                    Me.CurrentMaterialStream.Phases(7).Properties.massfraction = xs
                                    Me.CurrentMaterialStream.Phases(7).Properties.molarfraction = xs
                                    Me.CurrentMaterialStream.Phases(4).Properties.massfraction = 0.0#
                                    Me.CurrentMaterialStream.Phases(4).Properties.molarfraction = 0.0#

                                    i = 0
                                    For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                        subst.MoleFraction = vz(i)
                                        subst.FugacityCoeff = 1
                                        subst.ActivityCoeff = 1
                                        subst.PartialVolume = 0
                                        subst.PartialPressure = P
                                        subst.MassFraction = vz(i)
                                        i += 1
                                    Next

                                Else

                                    Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                                    Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                                    Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl
                                    Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv

                                    i = 0
                                    For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                        subst.MoleFraction = vz(i)
                                        subst.FugacityCoeff = 1
                                        subst.ActivityCoeff = 1
                                        subst.PartialVolume = 0
                                        subst.PartialPressure = P
                                        subst.MassFraction = vz(i)
                                        i += 1
                                    Next
                                    i = 0
                                    For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                        subst.MoleFraction = vz(i)
                                        subst.FugacityCoeff = 1
                                        subst.ActivityCoeff = 1
                                        subst.PartialVolume = 0
                                        subst.PartialPressure = P
                                        subst.MassFraction = vz(i)
                                        i += 1
                                    Next

                                End If

                            Else

redirect:                       result = Me.FlashBase.Flash_PH(RET_VMOL(Phase.Mixture), P, H, T, Me)

                                T = result(4)

                                xl = result(0)
                                xv = result(1)
                                xl2 = result(7)
                                xs = result(9)

                                Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                                Me.CurrentMaterialStream.Phases(4).Properties.molarfraction = xl2
                                Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                                Me.CurrentMaterialStream.Phases(7).Properties.molarfraction = xs

                                Dim Vx = result(2)
                                Dim Vy = result(3)
                                Dim Vx2 = result(8)
                                Dim Vs = result(10)

                                Dim FCL = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                                Dim FCL2 = Me.DW_CalcFugCoeff(Vx2, T, P, State.Liquid)
                                Dim FCV = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)
                                Dim FCS = Me.DW_CalcFugCoeff(Vs, T, P, State.Solid)

                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                    subst.MoleFraction = Vx(i)
                                    subst.FugacityCoeff = FCL(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                i = 1
                                For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 3)
                                    i += 1
                                Next
                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                    subst.MoleFraction = Vx2(i)
                                    subst.FugacityCoeff = FCL2(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                i = 1
                                For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 4)
                                    i += 1
                                Next
                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                    subst.MoleFraction = Vy(i)
                                    subst.FugacityCoeff = FCV(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                i = 1
                                For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 2)
                                    i += 1
                                Next
                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                    subst.MoleFraction = Vs(i)
                                    subst.FugacityCoeff = FCS(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 7)
                                Next

                                Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl * Me.AUX_MMM(Phase.Liquid1) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                                Me.CurrentMaterialStream.Phases(4).Properties.massfraction = xl2 * Me.AUX_MMM(Phase.Liquid2) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                                Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv * Me.AUX_MMM(Phase.Vapor) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                                Me.CurrentMaterialStream.Phases(7).Properties.massfraction = xs * Me.AUX_MMM(Phase.Solid) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))

                                Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                                For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                    constprops.Add(su.ConstantProperties)
                                Next

                                Dim SM, SV, SL, SL2, SS As Double

                                If xl <> 0 Then SL = Me.DW_CalcEntropy(Vx, T, P, State.Liquid)
                                If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid)
                                If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor)
                                If xs <> 0 And T <> 298.15 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15)
                                SM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * SL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * SL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * SV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * SS

                                S = SM

                            End If

                        Case FlashSpec.S

                            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

                            If Double.IsNaN(S) Or Double.IsInfinity(S) Then S = Me.CurrentMaterialStream.Phases(0).Properties.molar_entropy.GetValueOrDefault / Me.CurrentMaterialStream.Phases(0).Properties.molecularWeight.GetValueOrDefault

                            If Not T.IsValid Or Not S.IsValid Then Throw New ArgumentException("PS Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))
                            If Not T.IsPositive Then Throw New ArgumentException("PS Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))

                            If Me.AUX_IS_SINGLECOMP(Phase.Mixture) And Me.ComponentName <> "FPROPS" And Me.ComponentName <> "CoolProp" Then

                                Dim brentsolverT As New BrentOpt.Brent
                                brentsolverT.DefineFuncDelegate(AddressOf EntropyTx)

                                Dim hl, hv, sl, sv, Tsat As Double
                                Dim vz As Object = Me.RET_VMOL(Phase.Mixture)

                                P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

                                Tsat = 0.0#
                                For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                    Tsat += subst.MoleFraction * Me.AUX_TSATi(P, subst.Name)
                                Next

                                hl = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Liquid)
                                hv = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Vapor)
                                sl = Me.DW_CalcEntropy(vz, Tsat, P, State.Liquid)
                                sv = Me.DW_CalcEntropy(vz, Tsat, P, State.Vapor)
                                If S <= sl Then
                                    xv = 0
                                    LoopVarState = State.Liquid
                                ElseIf S >= sv Then
                                    xv = 1
                                    LoopVarState = State.Vapor
                                Else
                                    xv = (S - sl) / (sv - sl)
                                End If
                                If Tsat > Me.AUX_TCM(Phase.Mixture) Then
                                    xv = 1.0#
                                    LoopVarState = State.Vapor
                                End If
                                xl = 1 - xv

                                If xv <> 0.0# And xv <> 1.0# Then
                                    T = Tsat
                                    H = xv * hv + (1 - xv) * hl
                                Else
                                    LoopVarF = S
                                    LoopVarX = P
                                    T = brentsolverT.BrentOpt(Me.AUX_TFM(Phase.Mixture), 2000, 20, 0.0001, 1000, Nothing)
                                    If xv = 0.0# Then
                                        H = Me.DW_CalcEnthalpy(vz, T, P, State.Liquid)
                                    Else
                                        H = Me.DW_CalcEnthalpy(vz, T, P, State.Vapor)
                                    End If
                                End If

                                Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                                Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                                Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl
                                Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv

                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                    subst.MoleFraction = vz(i)
                                    subst.FugacityCoeff = 1
                                    subst.ActivityCoeff = 1
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = P
                                    subst.MassFraction = vz(i)
                                    i += 1
                                Next
                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                    subst.MoleFraction = vz(i)
                                    subst.FugacityCoeff = 1
                                    subst.ActivityCoeff = 1
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = P
                                    subst.MassFraction = vz(i)
                                    i += 1
                                Next

                            Else

redirect2:                      result = Me.FlashBase.Flash_PS(RET_VMOL(Phase.Mixture), P, S, T, Me)

                                T = result(4)

                                xl = result(0)
                                xv = result(1)
                                xl2 = result(7)
                                xs = result(9)

                                Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                                Me.CurrentMaterialStream.Phases(4).Properties.molarfraction = xl2
                                Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                                Me.CurrentMaterialStream.Phases(7).Properties.molarfraction = xs

                                Dim Vx = result(2)
                                Dim Vy = result(3)
                                Dim Vx2 = result(8)
                                Dim Vs = result(10)

                                Dim FCL = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                                Dim FCL2 = Me.DW_CalcFugCoeff(Vx2, T, P, State.Liquid)
                                Dim FCV = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)

                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                    subst.MoleFraction = Vx(i)
                                    subst.FugacityCoeff = FCL(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                i = 1
                                For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 3)
                                    i += 1
                                Next
                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                    subst.MoleFraction = Vx2(i)
                                    subst.FugacityCoeff = FCL2(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                i = 1
                                For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 4)
                                    i += 1
                                Next
                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                    subst.MoleFraction = Vy(i)
                                    subst.FugacityCoeff = FCV(i)
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next

                                i = 1
                                For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 2)
                                    i += 1
                                Next

                                i = 0
                                For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                    subst.MoleFraction = Vs(i)
                                    subst.FugacityCoeff = 1.0#
                                    subst.ActivityCoeff = 0
                                    subst.PartialVolume = 0
                                    subst.PartialPressure = 0
                                    i += 1
                                Next
                                For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 7)
                                Next

                                Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl * Me.AUX_MMM(Phase.Liquid1) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                                Me.CurrentMaterialStream.Phases(4).Properties.massfraction = xl2 * Me.AUX_MMM(Phase.Liquid2) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                                Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv * Me.AUX_MMM(Phase.Vapor) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                                Me.CurrentMaterialStream.Phases(7).Properties.massfraction = xs * Me.AUX_MMM(Phase.Solid) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))

                                Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                                For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                    constprops.Add(su.ConstantProperties)
                                Next

                                Dim HM, HV, HL, HL2, HS As Double

                                If xl <> 0 Then HL = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid)
                                If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid)
                                If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor)
                                If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops)
                                HM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * HL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * HL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * HV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * HS

                                H = HM

                            End If

                        Case FlashSpec.VAP

                            Dim KI(n) As Double
                            Dim HM, HV, HL, HL2, HS As Double
                            Dim SM, SV, SL, SL2, SS As Double

                            i = 0
                            Do
                                KI(i) = 0
                                i = i + 1
                            Loop Until i = n + 1

                            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault

                            If Not P.IsValid Or Not xv.IsValid Then Throw New ArgumentException("PVF Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))
                            If Not P.IsPositive Or xv.IsNegative Then Throw New ArgumentException("PVF Flash: " & Calculator.GetLocalString("ErrorInvalidFlashSpecValue"))

                            Dim Vx, Vx2, Vy, Vs As Double()

                            If Me.AUX_IS_SINGLECOMP(Phase.Mixture) Then

                                Dim Tsat As Double
                                Dim vz As Object = Me.RET_VMOL(Phase.Mixture)

                                Tsat = 0.0#
                                For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                    Tsat += subst.MoleFraction * Me.AUX_TSATi(P, subst.Name)
                                Next

                                HL = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Liquid)
                                HV = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Vapor)
                                SL = Me.DW_CalcEntropy(vz, Tsat, P, State.Liquid)
                                SV = Me.DW_CalcEntropy(vz, Tsat, P, State.Vapor)
                                H = xv * HV + (1 - xv) * HL
                                S = xv * SV + (1 - xv) * SL
                                T = Tsat
                                xl = 1 - xv
                                xl2 = 0.0#

                                Vx = vz
                                Vy = vz
                                Vx2 = vz
                                Vs = vz

                            Else

                                result = Me.FlashBase.Flash_PV(RET_VMOL(Phase.Mixture), P, xv, T, Me)

                                T = result(4)

                                xl = result(0)
                                xv = result(1)
                                xl2 = result(7)
                                xs = result(9)

                                Vx = result(2)
                                Vy = result(3)
                                Vx2 = result(8)
                                Vs = result(10)

                            End If

                            Me.CurrentMaterialStream.Phases(3).Properties.molarfraction = xl
                            Me.CurrentMaterialStream.Phases(4).Properties.molarfraction = xl2
                            Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = xv
                            Me.CurrentMaterialStream.Phases(7).Properties.molarfraction = xs

                            Dim FCL = Me.DW_CalcFugCoeff(Vx, T, P, State.Liquid)
                            Dim FCL2 = Me.DW_CalcFugCoeff(Vx2, T, P, State.Liquid)
                            Dim FCV = Me.DW_CalcFugCoeff(Vy, T, P, State.Vapor)

                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                subst.MoleFraction = Vx(i)
                                subst.FugacityCoeff = FCL(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 3)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                subst.MoleFraction = Vx2(i)
                                subst.FugacityCoeff = FCL2(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 4)
                            Next
                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                subst.MoleFraction = Vy(i)
                                subst.FugacityCoeff = FCV(i)
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 2)
                            Next

                            i = 0
                            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                subst.MoleFraction = Vs(i)
                                subst.FugacityCoeff = 1.0#
                                subst.ActivityCoeff = 0
                                subst.PartialVolume = 0
                                subst.PartialPressure = 0
                                i += 1
                            Next
                            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                                subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, 7)
                            Next

                            Me.CurrentMaterialStream.Phases(3).Properties.massfraction = xl * Me.AUX_MMM(Phase.Liquid1) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(4).Properties.massfraction = xl2 * Me.AUX_MMM(Phase.Liquid2) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(2).Properties.massfraction = xv * Me.AUX_MMM(Phase.Vapor) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))
                            Me.CurrentMaterialStream.Phases(7).Properties.massfraction = xs * Me.AUX_MMM(Phase.Solid) / (xl * Me.AUX_MMM(Phase.Liquid1) + xl2 * Me.AUX_MMM(Phase.Liquid2) + xv * Me.AUX_MMM(Phase.Vapor) + xs * Me.AUX_MMM(Phase.Solid))

                            Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                            For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                                constprops.Add(su.ConstantProperties)
                            Next

                            If xl <> 0 Then HL = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid)
                            If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid)
                            If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor)
                            If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops)
                            HM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * HL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * HL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * HV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * HS

                            H = HM

                            If xl <> 0 Then SL = Me.DW_CalcEntropy(Vx, T, P, State.Liquid)
                            If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid)
                            If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor)
                            If xs <> 0 And T <> 298.15 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15)

                            SM = Me.CurrentMaterialStream.Phases(4).Properties.massfraction.GetValueOrDefault * SL2 + Me.CurrentMaterialStream.Phases(3).Properties.massfraction.GetValueOrDefault * SL + Me.CurrentMaterialStream.Phases(2).Properties.massfraction.GetValueOrDefault * SV + Me.CurrentMaterialStream.Phases(7).Properties.massfraction.GetValueOrDefault * SS

                            S = SM

                    End Select

            End Select

            If Calculator.CAPEOPENMode Then
                Dim summf As Double = 0, sumwf As Double = 0
                For Each pi As PhaseInfo In Me.PhaseMappings.Values
                    If Not pi.PhaseLabel = "Disabled" Then
                        summf += Me.CurrentMaterialStream.Phases(pi.DWPhaseIndex).Properties.molarfraction.GetValueOrDefault
                        sumwf += Me.CurrentMaterialStream.Phases(pi.DWPhaseIndex).Properties.massfraction.GetValueOrDefault
                    End If
                Next
                If Abs(summf - 1) > 0.0001 Then
                    For Each pi As PhaseInfo In Me.PhaseMappings.Values
                        If Not pi.PhaseLabel = "Disabled" Then
                            If Not Me.CurrentMaterialStream.Phases(pi.DWPhaseIndex).Properties.molarfraction.HasValue Then
                                Me.CurrentMaterialStream.Phases(pi.DWPhaseIndex).Properties.molarfraction = 1 - summf
                                Me.CurrentMaterialStream.Phases(pi.DWPhaseIndex).Properties.massfraction = 1 - sumwf
                            End If
                        End If
                    Next
                End If
            End If

            With Me.CurrentMaterialStream

                .Phases(0).Properties.temperature = T
                .Phases(0).Properties.pressure = P
                .Phases(0).Properties.enthalpy = H
                .Phases(0).Properties.entropy = S

            End With

            Me.CurrentMaterialStream.AtEquilibrium = True

        End Sub

        Private Function EnthalpyTx(ByVal x As Double, ByVal otherargs As Object) As Double

            Dim er As Double = LoopVarF - Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), x, LoopVarX, LoopVarState)
            Return er

        End Function

        Private Function EnthalpyPx(ByVal x As Double, ByVal otherargs As Object) As Double

            Dim er As Double = LoopVarF - Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), LoopVarX, x, LoopVarState)
            Return er

        End Function

        Private Function EntropyTx(ByVal x As Double, ByVal otherargs As Object) As Double

            Dim er As Double = LoopVarF - Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), x, LoopVarX, LoopVarState)
            Return er

        End Function

        Private Function EntropyPx(ByVal x As Double, ByVal otherargs As Object) As Double

            Dim er As Double = LoopVarF - Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), LoopVarX, x, LoopVarState)
            Return er

        End Function

        Public Sub DW_CalcVazaoMassica()
            With Me.CurrentMaterialStream
                .Phases(0).Properties.massflow = .Phases(0).Properties.molarflow.GetValueOrDefault * Me.AUX_MMM(Phase.Mixture) / 1000
            End With
        End Sub

        Public Sub DW_CalcVazaoMolar()
            With Me.CurrentMaterialStream
                .Phases(0).Properties.molarflow = .Phases(0).Properties.massflow.GetValueOrDefault / Me.AUX_MMM(Phase.Mixture) * 1000
            End With
        End Sub

        Public Sub DW_CalcVazaoVolumetrica()
            With Me.CurrentMaterialStream
                If .Phases(0).Properties.density.GetValueOrDefault > 0 Then
                    If .Phases(0).Properties.density.GetValueOrDefault > 0 Then
                        .Phases(0).Properties.volumetric_flow = .Phases(0).Properties.massflow.GetValueOrDefault / .Phases(0).Properties.density.GetValueOrDefault
                    Else
                        .Phases(0).Properties.volumetric_flow = 0.0#
                    End If
                Else
                    .Phases(0).Properties.volumetric_flow = 0.0#
                End If
            End With
        End Sub

        Public MustOverride Function DW_CalcMassaEspecifica_ISOL(ByVal Phase1 As Phase, ByVal T As Double, ByVal P As Double, Optional ByVal Pvp As Double = 0) As Double

        Public MustOverride Function DW_CalcViscosidadeDinamica_ISOL(ByVal Phase1 As Phase, ByVal T As Double, ByVal P As Double) As Double

        Public MustOverride Function DW_CalcTensaoSuperficial_ISOL(ByVal Phase1 As Phase, ByVal T As Double, ByVal P As Double) As Double

        ''' <summary>
        ''' Calculates thermodynamic equilibrium for the current material stream without updating results, returning them as an object.
        ''' </summary>
        ''' <param name="spec1">P, T, H or S</param>
        ''' <param name="spec2">P, T, H or S</param>
        ''' <param name="val1">spec1 value</param>
        ''' <param name="val2">spec2 value</param>
        ''' <param name="estimate"></param>
        ''' <returns>A vector containing compositions and state variables.</returns>
        ''' <remarks></remarks>
        Public Overridable Function DW_CalcEquilibrio_ISOL(ByVal spec1 As FlashSpec, ByVal spec2 As FlashSpec, ByVal val1 As Double, ByVal val2 As Double, ByVal estimate As Double) As Object

            Try
                Me._tpseverity = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ThreePhaseFlashStabTestSeverity
                Me._tpcompids = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ThreePhaseFlashStabTestCompIds
            Catch ex As Exception
                Me._tpseverity = 0
                Me._tpcompids = New String() {}
            End Try

            Dim P, T, H, S, xv, xl, xl2, xs As Double
            Dim result As Object = Nothing
            Dim n As Integer = Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1
            Dim Vx2(n), Vx(n), Vy(n), Vs(n) As Double
            Dim i As Integer = 0

            Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
            For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                constprops.Add(su.ConstantProperties)
            Next

            Select Case spec1

                Case FlashSpec.T

                    Select Case spec2

                        Case FlashSpec.P

                            T = val1
                            P = val2

                            Dim ige As Double = 0.0#
                            Dim fge As Double = 0.0#
                            Dim dge As Double = 0.0#

                            If Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ValidateEquilibriumCalc Then

                                ige = Me.DW_CalcGibbsEnergy(RET_VMOL(Phase.Mixture), T, P)

                            End If

                            result = Me.FlashBase.Flash_PT(RET_VMOL(Phase.Mixture), P, T, Me)

                            xl = result(0)
                            xv = result(1)
                            xl2 = result(5)
                            xs = result(7)

                            Vx = result(2)
                            Vy = result(3)
                            Vx2 = result(6)
                            Vs = result(8)

                            If Not Calculator.CAPEOPENMode Then
                                If Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ValidateEquilibriumCalc _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE _
                                And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then

                                    fge = xl * Me.DW_CalcGibbsEnergy(Vx, T, P)
                                    fge += xl2 * Me.DW_CalcGibbsEnergy(Vx2, T, P)
                                    fge += xv * Me.DW_CalcGibbsEnergy(Vy, T, P)

                                    dge = fge - ige

                                    Dim dgtol As Double = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.FlashValidationDGETolerancePct

                                    If dge > 0.0# And Math.Abs(dge / ige * 100) > Math.Abs(dgtol) Then
                                        Throw New Exception(Calculator.GetLocalString("InvalidFlashResult") & "(DGE = " & dge & " kJ/kg, " & Format(dge / ige * 100, "0.00") & "%)")
                                    End If

                                End If
                            End If

                            Dim MWM As Double = xl * Me.AUX_MMM(Vx, "L") + xl2 * Me.AUX_MMM(Vx2, "L") + xv * Me.AUX_MMM(Vy, "V") + xs * Me.AUX_MMM(Vs, "S")

                            Dim HM, HV, HL1, HL2, HS As Double

                            If xl <> 0 Then HL1 = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                            If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                            If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                            If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) * Me.AUX_MMM(Vs, "S")
                            HM = (xl * HL1 + xl2 * HL2 + xv * HV + xs * HS) / MWM

                            H = HM

                            Dim SM, SV, SL1, SL2, SS As Double

                            If xl <> 0 Then SL1 = Me.DW_CalcEntropy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                            If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                            If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                            If xs <> 0 And T <> 298.15 Then If xs <> 0 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15) * Me.AUX_MMM(Vs, "S")

                            SM = (xl * SL1 + xl2 * SL2 + xv * SV + xs * SS) / MWM

                            S = SM

                        Case FlashSpec.H

                            Throw New Exception(Calculator.GetLocalString("PropPack_FlashTHNotSupported"))

                        Case FlashSpec.S

                            Throw New Exception(Calculator.GetLocalString("PropPack_FlashTSNotSupported"))

                        Case FlashSpec.VAP

                            Dim KI(n) As Double

                            i = 0
                            Do
                                KI(i) = 0
                                i = i + 1
                            Loop Until i = n + 1

                            If estimate <> 0 Then
                                P = estimate
                            Else
                                P = 0
                            End If
                            T = val1
                            xv = val2

                            result = Me.FlashBase.Flash_TV(RET_VMOL(Phase.Mixture), T, xv, P, Me)

                            P = result(4)
                            Vx = result(2)
                            Vx2 = result(8)
                            Vy = result(3)
                            Vs = result(10)
                            xl = result(0)
                            xl2 = result(7)
                            xs = result(9)

                            Dim HM, HV, HL1, HL2, HS As Double

                            Dim MWM As Double = xl * Me.AUX_MMM(Vx, "L") + xl2 * Me.AUX_MMM(Vx2, "L") + xv * Me.AUX_MMM(Vy, "V") + xs * Me.AUX_MMM(Vs, "S")

                            If xl <> 0 Then HL1 = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                            If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                            If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                            If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) * Me.AUX_MMM(Vs, "S")
                            HM = (xl * HL1 + xl2 * HL2 + xv * HV + xs * HS) / MWM

                            H = HM

                            Dim SM, SV, SL1, SL2, SS As Double

                            If xl <> 0 Then SL1 = Me.DW_CalcEntropy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                            If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                            If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                            If xs <> 0 And T <> 298.15 Then If xs <> 0 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15) * Me.AUX_MMM(Vs, "S")

                            SM = (xl * SL1 + xl2 * SL2 + xv * SV + xs * SS) / MWM

                            S = SM

                    End Select

                Case FlashSpec.P

                    Select Case spec2

                        Case FlashSpec.H

                            If estimate <> 0 Then
                                T = estimate
                            Else
                                T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            End If
                            P = val1
                            H = val2

                            If Me.AUX_IS_SINGLECOMP(Phase.Mixture) And Me.ComponentName <> "FPROPS" Then

                                Dim brentsolverT As New BrentOpt.Brent
                                brentsolverT.DefineFuncDelegate(AddressOf EnthalpyTx)

                                Dim hl, hv, sl, sv, Tsat As Double
                                Dim vz As Object = Me.RET_VMOL(Phase.Mixture)

                                Tsat = 0.0#
                                For i = 0 To n
                                    Tsat += vz(i) * Me.AUX_TSATi(P, i)
                                Next

                                Dim Tf As Double = AUX_TFM(Phase.Mixture)

                                If Tsat < Tf Then Tsat = Tf + 0.1

                                hl = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Liquid)
                                hv = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Vapor)
                                sl = Me.DW_CalcEntropy(vz, Tsat, P, State.Liquid)
                                sv = Me.DW_CalcEntropy(vz, Tsat, P, State.Vapor)
                                If H <= hl Then
                                    xv = 0
                                    LoopVarState = State.Liquid
                                ElseIf H >= hv Then
                                    xv = 1
                                    LoopVarState = State.Vapor
                                Else
                                    xv = (H - hl) / (hv - hl)
                                End If
                                If Tsat > Me.AUX_TCM(Phase.Mixture) Then
                                    xv = 1.0#
                                    LoopVarState = State.Vapor
                                End If
                                xl = 1 - xv

                                If xv <> 0.0# And xv <> 1.0# Then
                                    T = Tsat
                                    S = xv * sv + (1 - xv) * sl
                                Else
                                    LoopVarF = H
                                    LoopVarX = P
                                    T = brentsolverT.BrentOpt(Me.AUX_TFM(Phase.Mixture), 2000, 20, 0.000000000001, 1000, Nothing)
                                    If xv = 0.0# Then
                                        S = Me.DW_CalcEntropy(vz, T, P, State.Liquid)
                                    Else
                                        S = Me.DW_CalcEntropy(vz, T, P, State.Vapor)
                                    End If
                                End If

                                Vx = vz
                                Vy = vz

                            Else

redirect:                       result = Me.FlashBase.Flash_PH(RET_VMOL(Phase.Mixture), P, H, T, Me)

                                T = result(4)
                                xl = result(0)
                                xv = result(1)
                                xl2 = result(7)
                                xs = result(9)

                                Vx = result(2)
                                Vy = result(3)
                                Vx2 = result(8)
                                Vs = result(10)

                                Dim MWM As Double = xl * Me.AUX_MMM(Vx, "L") + xl2 * Me.AUX_MMM(Vx2, "L") + xv * Me.AUX_MMM(Vy, "V") + xs * Me.AUX_MMM(Vs, "S")

                                'Dim HM, HV, HL1, HL2, HS As Double

                                'If xl <> 0 Then HL1 = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                                'If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                                'If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                                'If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) * Me.AUX_MMM(Vs, "S")
                                'HM = (xl * HL1 + xl2 * HL2 + xv * HV + xs * HS) / MWM

                                'H = HM

                                Dim SM, SV, SL1, SL2, SS As Double

                                If xl <> 0 Then SL1 = Me.DW_CalcEntropy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                                If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                                If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                                If xs <> 0 And T <> 298.15 Then If xs <> 0 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15) * Me.AUX_MMM(Vs, "S")

                                SM = (xl * SL1 + xl2 * SL2 + xv * SV + xs * SS) / MWM

                                S = SM

                            End If

                        Case FlashSpec.S

                            If estimate <> 0 Then
                                T = estimate
                            Else
                                T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
                            End If
                            P = val1
                            S = val2

                            If Me.AUX_IS_SINGLECOMP(Phase.Mixture) And Me.ComponentName <> "FPROPS" And Me.ComponentName <> "CoolProp" Then

                                Dim brentsolverT As New BrentOpt.Brent
                                brentsolverT.DefineFuncDelegate(AddressOf EntropyTx)

                                Dim hl, hv, sl, sv, Tsat As Double
                                Dim vz As Object = Me.RET_VMOL(Phase.Mixture)

                                Tsat = 0.0#
                                For i = 0 To n
                                    Tsat += vz(i) * Me.AUX_TSATi(P, i)
                                Next

                                hl = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Liquid)
                                hv = Me.DW_CalcEnthalpy(vz, Tsat, P, State.Vapor)
                                sl = Me.DW_CalcEntropy(vz, Tsat, P, State.Liquid)
                                sv = Me.DW_CalcEntropy(vz, Tsat, P, State.Vapor)
                                If S <= sl Then
                                    xv = 0
                                    LoopVarState = State.Liquid
                                ElseIf S >= sv Then
                                    xv = 1
                                    LoopVarState = State.Vapor
                                Else
                                    xv = (S - sl) / (sv - sl)
                                End If
                                If Tsat > Me.AUX_TCM(Phase.Mixture) Then
                                    xv = 1.0#
                                    LoopVarState = State.Vapor
                                End If
                                xl = 1 - xv

                                If xv <> 0.0# And xv <> 1.0# Then
                                    T = Tsat
                                    H = xv * hv + (1 - xv) * hl
                                Else
                                    LoopVarF = S
                                    LoopVarX = P
                                    T = brentsolverT.BrentOpt(Me.AUX_TFM(Phase.Mixture), 2000, 20, 0.000000000001, 1000, Nothing)
                                    If xv = 0.0# Then
                                        H = Me.DW_CalcEnthalpy(vz, T, P, State.Liquid)
                                    Else
                                        H = Me.DW_CalcEnthalpy(vz, T, P, State.Vapor)
                                    End If
                                End If

                                Vx = vz
                                Vy = vz

                            Else

redirect2:                      result = Me.FlashBase.Flash_PS(RET_VMOL(Phase.Mixture), P, S, T, Me)

                                T = result(4)
                                xl = result(0)
                                xv = result(1)
                                xl2 = result(7)
                                xs = result(9)

                                Vx = result(2)
                                Vy = result(3)
                                Vx2 = result(8)
                                Vs = result(10)

                                Dim MWM As Double = xl * Me.AUX_MMM(Vx, "L") + xl2 * Me.AUX_MMM(Vx2, "L") + xv * Me.AUX_MMM(Vy, "V") + xs * Me.AUX_MMM(Vs, "S")

                                Dim HM, HV, HL1, HL2, HS As Double

                                If xl <> 0 Then HL1 = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                                If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                                If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                                If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) * Me.AUX_MMM(Vs, "S")
                                HM = (xl * HL1 + xl2 * HL2 + xv * HV + xs * HS) / MWM

                                H = HM

                                'Dim SM, SV, SL1, SL2, SS As Double

                                'If xl <> 0 Then SL1 = Me.DW_CalcEntropy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                                'If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                                'If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                                'If xs <> 0 And T <> 298.15 Then If xs <> 0 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15) * Me.AUX_MMM(Vs, "S")

                                'SM = (xl * SL1 + xl2 * SL2 + xv * SV + xs * SS) / MWM

                                'S = SM

                            End If

                        Case FlashSpec.VAP

                            Dim KI(n) As Double

                            i = 0
                            Do
                                KI(i) = 0
                                i = i + 1
                            Loop Until i = n + 1

                            If estimate <> 0 Then
                                T = estimate
                            Else
                                T = 0
                            End If
                            P = val1
                            xv = val2

                            result = Me.FlashBase.Flash_PV(RET_VMOL(Phase.Mixture), P, xv, 0, Me)

                            T = result(4)
                            Vx = result(2)
                            Vx2 = result(8)
                            Vy = result(3)
                            Vs = result(10)
                            xl = result(0)
                            xl2 = result(7)
                            xs = result(9)

                            Dim HM, HV, HL1, HL2, HS As Double

                            Dim MWM As Double = xl * Me.AUX_MMM(Vx, "L") + xl2 * Me.AUX_MMM(Vx2, "L") + xv * Me.AUX_MMM(Vy, "V") + xs * Me.AUX_MMM(Vs, "S")

                            If xl <> 0 Then HL1 = Me.DW_CalcEnthalpy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                            If xl2 <> 0 Then HL2 = Me.DW_CalcEnthalpy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                            If xv <> 0 Then HV = Me.DW_CalcEnthalpy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                            If xs <> 0 Then HS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) * Me.AUX_MMM(Vs, "S")
                            HM = (xl * HL1 + xl2 * HL2 + xv * HV + xs * HS) / MWM

                            H = HM

                            Dim SM, SV, SL1, SL2, SS As Double

                            If xl <> 0 Then SL1 = Me.DW_CalcEntropy(Vx, T, P, State.Liquid) * Me.AUX_MMM(Vx, "L")
                            If xl2 <> 0 Then SL2 = Me.DW_CalcEntropy(Vx2, T, P, State.Liquid) * Me.AUX_MMM(Vx2, "L")
                            If xv <> 0 Then SV = Me.DW_CalcEntropy(Vy, T, P, State.Vapor) * Me.AUX_MMM(Vy, "V")
                            If xs <> 0 And T <> 298.15 Then If xs <> 0 Then SS = Me.DW_CalcSolidEnthalpy(T, Vs, constprops) / (T - 298.15) * Me.AUX_MMM(Vs, "S")

                            SM = (xl * SL1 + xl2 * SL2 + xv * SV + xs * SS) / MWM

                            S = SM

                    End Select

            End Select

            Return New Object() {xl, xv, T, P, H, S, 1, 1, Vx, Vy, result, xl2, Vx2, xs, Vs}

        End Function

        Public MustOverride Function DW_CalcEnergyFlowMistura_ISOL(ByVal T As Double, ByVal P As Double) As Double

        Public MustOverride Function DW_CalcCp_ISOL(ByVal Phase1 As PropertyPackages.Phase, ByVal T As Double, ByVal P As Double) As Double

        Public MustOverride Function DW_CalcCv_ISOL(ByVal Phase1 As PropertyPackages.Phase, ByVal T As Double, ByVal P As Double) As Double

        Public MustOverride Function DW_CalcK_ISOL(ByVal Phase1 As PropertyPackages.Phase, ByVal T As Double, ByVal P As Double) As Double

        Public MustOverride Function DW_CalcMM_ISOL(ByVal Phase1 As PropertyPackages.Phase, ByVal T As Double, ByVal P As Double) As Double

        Public MustOverride Function DW_CalcPVAP_ISOL(ByVal T As Double) As Double

        ''' <summary>
        ''' This function returns points to build the phase envelope.
        ''' </summary>
        ''' <param name="parameters"></param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Overridable Function DW_ReturnPhaseEnvelope(ByVal parameters As Object, Optional ByVal bw As System.ComponentModel.BackgroundWorker = Nothing) As Object

            If Calculator.EnableGPUProcessing Then Calculator.InitComputeDevice()

            Dim cpc As New Utilities.TCP.Methods

            Dim i As Integer

            Dim n As Integer = Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1

            Dim Vz(n) As Double
            Dim comp As Interfaces.ICompound

            i = 0
            For Each comp In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                Vz(i) += comp.MoleFraction.GetValueOrDefault
                i += 1
            Next

            Dim j, k, l As Integer
            i = 0
            Do
                If Vz(i) = 0 Then j += 1
                i = i + 1
            Loop Until i = n + 1

            Dim VTc(n), Vpc(n), Vw(n), VVc(n), VKij(n, n) As Double
            Dim Vm2(UBound(Vz) - j), VPc2(UBound(Vz) - j), VTc2(UBound(Vz) - j), VVc2(UBound(Vz) - j), Vw2(UBound(Vz) - j), VKij2(UBound(Vz) - j, UBound(Vz) - j)

            VTc = Me.RET_VTC()
            Vpc = Me.RET_VPC()
            VVc = Me.RET_VVC()
            Vw = Me.RET_VW()
            VKij = Me.RET_VKij

            i = 0
            k = 0
            Do
                If Vz(i) <> 0 Then
                    Vm2(k) = Vz(i)
                    VTc2(k) = VTc(i)
                    VPc2(k) = Vpc(i)
                    VVc2(k) = VVc(i)
                    Vw2(k) = Vw(i)
                    j = 0
                    l = 0
                    Do
                        If Vz(l) <> 0 Then
                            VKij2(k, j) = VKij(i, l)
                            j = j + 1
                        End If
                        l = l + 1
                    Loop Until l = n + 1
                    k = k + 1
                End If
                i = i + 1
            Loop Until i = n + 1

            Dim Pmin, Tmin, dP, dT, T, P As Double
            Dim PB, PO, TVB, TVD, HB, HO, SB, SO, VB, VO, TE, PE, TH, PHsI, PHsII, TQ, PQ, TI, PI, TOWF, POWF, VOWF, HOWF, SOWF As New ArrayList
            Dim TCR, PCR, VCR As Double

            Dim CP As New ArrayList
            If n > 0 Then
                CP = cpc.CRITPT_PR(Vm2, VTc2, VPc2, VVc2, Vw2, VKij2)
                If CP.Count > 0 Then
                    Dim cp0 = CP(0)
                    TCR = cp0(0)
                    PCR = cp0(1)
                    VCR = cp0(2)
                Else
                    TCR = 0
                    PCR = 0
                    VCR = 0
                End If
            Else
                TCR = Me.AUX_TCM(Phase.Mixture)
                PCR = Me.AUX_PCM(Phase.Mixture)
                VCR = Me.AUX_VCM(Phase.Mixture)
                CP.Add(New Object() {TCR, PCR, VCR})
            End If

            Pmin = 101325
            Tmin = 0.3 * TCR

            dP = (PCR - Pmin) / 30
            dT = (TCR - Tmin) / 30

            Dim beta As Double = 10

            Dim tmp2 As Object
            Dim KI(n) As Double

            j = 0
            Do
                KI(j) = 0
                j = j + 1
            Loop Until j = n + 1

            i = 0
            P = Pmin
            T = Tmin
            Do
                If i < 2 Then
                    Try
                        tmp2 = Me.FlashBase.Flash_PV(Me.RET_VMOL(Phase.Mixture), P, 0, 0, Me)
                        TVB.Add(tmp2(4))
                        PB.Add(P)
                        T = TVB(i)
                        HB.Add(Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), T, P, State.Liquid))
                        SB.Add(Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), T, P, State.Liquid))
                        VB.Add(1 / Me.AUX_LIQDENS(T, P, 0, 0, False) * Me.AUX_MMM(Phase.Mixture))
                        KI = tmp2(6)
                    Catch ex As Exception

                    Finally
                        P = P + dP
                    End Try
                Else
                    If beta < 20 Then
                        Try
                            tmp2 = Me.FlashBase.Flash_TV(Me.RET_VMOL(Phase.Mixture), T, 0, PB(i - 1), Me, True, KI)
                            TVB.Add(T)
                            PB.Add(tmp2(4))
                            P = PB(i)
                            HB.Add(Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), T, P, State.Liquid))
                            SB.Add(Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), T, P, State.Liquid))
                            VB.Add(1 / Me.AUX_LIQDENS(T, P, 0, 0, False) * Me.AUX_MMM(Phase.Mixture))
                            KI = tmp2(6)
                        Catch ex As Exception

                        Finally
                            If Math.Abs(T - TCR) / TCR < 0.01 And Math.Abs(P - PCR) / PCR < 0.02 Then
                                T = T + dT * 0.5
                            Else
                                T = T + dT
                            End If
                        End Try
                    Else
                        Try
                            tmp2 = Me.FlashBase.Flash_PV(Me.RET_VMOL(Phase.Mixture), P, 0, TVB(i - 1), Me, True, KI)
                            TVB.Add(tmp2(4))
                            PB.Add(P)
                            T = TVB(i)
                            HB.Add(Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), T, P, State.Liquid))
                            SB.Add(Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), T, P, State.Liquid))
                            VB.Add(1 / Me.AUX_LIQDENS(T, P, 0, 0, False) * Me.AUX_MMM(Phase.Mixture))
                            KI = tmp2(6)
                        Catch ex As Exception

                        Finally
                            If Math.Abs(T - TCR) / TCR < 0.01 And Math.Abs(P - PCR) / PCR < 0.01 Then
                                P = P + dP * 0.1
                            Else
                                P = P + dP
                            End If
                        End Try
                    End If
                    beta = (Math.Log(PB(i) / 101325) - Math.Log(PB(i - 1) / 101325)) / (Math.Log(TVB(i)) - Math.Log(TVB(i - 1)))
                End If
                If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "Bubble Points (" & i + 1 & "/300)")
                i = i + 1
            Loop Until i >= 300 Or PB(i - 1) = 0 Or PB(i - 1) < 0 Or TVB(i - 1) < 0 Or _
                        T >= TCR Or Double.IsNaN(PB(i - 1)) = True Or _
                        Double.IsNaN(TVB(i - 1)) = True Or Math.Abs(T - TCR) / TCR < 0.002 And _
                        Math.Abs(P - PCR) / PCR < 0.002

            Dim Switch = False

            beta = 10

            j = 0
            Do
                KI(j) = 0
                j = j + 1
            Loop Until j = n + 1

            i = 0
            P = Pmin
            Do
                If i < 2 Then
                    Try
                        tmp2 = Me.FlashBase.Flash_PV(Me.RET_VMOL(Phase.Mixture), P, 1, 0, Me)
                        TVD.Add(tmp2(4))
                        PO.Add(P)
                        T = TVD(i)
                        HO.Add(Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), T, P, State.Vapor))
                        SO.Add(Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), T, P, State.Vapor))
                        VO.Add(1 / Me.AUX_VAPDENS(T, P) * Me.AUX_MMM(Phase.Mixture))
                        KI = tmp2(6)
                    Catch ex As Exception
                    Finally
                        If Math.Abs(T - TCR) / TCR < 0.01 And Math.Abs(P - PCR) / PCR < 0.01 Then
                            P = P + dP * 0.1
                        Else
                            P = P + dP
                        End If
                    End Try
                Else
                    If Abs(beta) < 2 Then
                        Try
                            tmp2 = Me.FlashBase.Flash_TV(Me.RET_VMOL(Phase.Mixture), T, 1, PO(i - 1), Me, True, KI)
                            TVD.Add(T)
                            PO.Add(tmp2(4))
                            P = PO(i)
                            HO.Add(Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), T, P, State.Vapor))
                            SO.Add(Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), T, P, State.Vapor))
                            VO.Add(1 / Me.AUX_VAPDENS(T, P) * Me.AUX_MMM(Phase.Mixture))
                            KI = tmp2(6)
                        Catch ex As Exception
                        Finally
                            If TVD(i) - TVD(i - 1) <= 0 Then
                                If Math.Abs(T - TCR) / TCR < 0.02 And Math.Abs(P - PCR) / PCR < 0.02 Then
                                    T = T - dT * 0.1
                                Else
                                    T = T - dT
                                End If
                            Else
                                If Math.Abs(T - TCR) / TCR < 0.02 And Math.Abs(P - PCR) / PCR < 0.02 Then
                                    T = T + dT * 0.1
                                Else
                                    T = T + dT
                                End If
                            End If
                        End Try
                    Else
                        Try
                            tmp2 = Me.FlashBase.Flash_PV(Me.RET_VMOL(Phase.Mixture), P, 1, TVD(i - 1), Me, False, KI)
                            TVD.Add(tmp2(4))
                            PO.Add(P)
                            T = TVD(i)
                            HO.Add(Me.DW_CalcEnthalpy(Me.RET_VMOL(Phase.Mixture), T, P, State.Vapor))
                            SO.Add(Me.DW_CalcEntropy(Me.RET_VMOL(Phase.Mixture), T, P, State.Vapor))
                            VO.Add(1 / Me.AUX_VAPDENS(T, P) * Me.AUX_MMM(Phase.Mixture))
                            KI = tmp2(6)
                        Catch ex As Exception
                        Finally
                            If Math.Abs(T - TCR) / TCR < 0.05 And Math.Abs(P - PCR) / PCR < 0.05 Then
                                P = P + dP * 0.25
                            Else
                                P = P + dP
                            End If
                        End Try
                    End If
                    If i >= PO.Count Then
                        i = i - 1
                    End If
                    beta = (Math.Log(PO(i) / 101325) - Math.Log(PO(i - 1) / 101325)) / (Math.Log(TVD(i)) - Math.Log(TVD(i - 1)))
                    If Double.IsNaN(beta) Or Double.IsInfinity(beta) Then beta = 0
                End If
                If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "Dew Points (" & i + 1 & "/300)")
                i = i + 1
            Loop Until i >= 300 Or PO(i - 1) = 0 Or PO(i - 1) < 0 Or TVD(i - 1) < 0 Or _
                        Double.IsNaN(PO(i - 1)) = True Or Double.IsNaN(TVD(i - 1)) = True Or _
                        Math.Abs(T - TCR) / TCR < 0.03 And Math.Abs(P - PCR) / PCR < 0.03

            beta = 10

            If CBool(parameters(2)) = True Then

                j = 0
                Do
                    KI(j) = 0
                    j = j + 1
                Loop Until j = n + 1

                i = 0
                P = 400000
                T = TVD(0)
                Do
                    If i < 2 Then
                        Try
                            tmp2 = Me.FlashBase.Flash_PV(Me.RET_VMOL(Phase.Mixture), P, parameters(1), 0, Me, False, KI)
                            TQ.Add(tmp2(4))
                            PQ.Add(P)
                            T = TQ(i)
                            KI = tmp2(6)
                        Catch ex As Exception
                        Finally
                            P = P + dP
                        End Try
                    Else
                        If beta < 2 Then
                            Try
                                tmp2 = Me.FlashBase.Flash_TV(Me.RET_VMOL(Phase.Mixture), T, parameters(1), PQ(i - 1), Me, True, KI)
                                TQ.Add(T)
                                PQ.Add(tmp2(4))
                                P = PQ(i)
                                KI = tmp2(6)
                            Catch ex As Exception
                            Finally
                                If Math.Abs(T - TCR) / TCR < 0.1 And Math.Abs(P - PCR) / PCR < 0.2 Then
                                    T = T + dT * 0.25
                                Else
                                    T = T + dT
                                End If
                            End Try
                        Else
                            Try
                                tmp2 = Me.FlashBase.Flash_PV(Me.RET_VMOL(Phase.Mixture), P, parameters(1), TQ(i - 1), Me, True, KI)
                                TQ.Add(tmp2(4))
                                PQ.Add(P)
                                T = TQ(i)
                                KI = tmp2(6)
                            Catch ex As Exception
                            Finally
                                If Math.Abs(T - TCR) / TCR < 0.1 And Math.Abs(P - PCR) / PCR < 0.1 Then
                                    P = P + dP * 0.1
                                Else
                                    P = P + dP
                                End If
                            End Try
                        End If
                        beta = (Math.Log(PQ(i) / 101325) - Math.Log(PQ(i - 1) / 101325)) / (Math.Log(TQ(i)) - Math.Log(TQ(i - 1)))
                    End If
                    If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "Quality Line (" & i + 1 & "/300)")
                    i = i + 1
                    If i > 2 Then
                        If PQ(i - 1) = PQ(i - 2) Or TQ(i - 1) = TQ(i - 2) Then Exit Do
                    End If
                Loop Until i >= 300 Or PQ(i - 1) = 0 Or PQ(i - 1) < 0 Or TQ(i - 1) < 0 Or _
                            Double.IsNaN(PQ(i - 1)) = True Or Double.IsNaN(TQ(i - 1)) = True Or _
                            Math.Abs(T - TCR) / TCR < 0.02 And Math.Abs(P - PCR) / PCR < 0.02
            Else
                TQ.Add(0)
                PQ.Add(0)
            End If

            If n > 0 And CBool(parameters(3)) = True Then
                If bw IsNot Nothing Then bw.ReportProgress(0, "Stability Line")
                Dim res As ArrayList = cpc.STABILITY_CURVE(Vm2, VTc2, VPc2, VVc2, Vw2, VKij2)
                i = 0
                Do
                    TE.Add(res(i)(0))
                    PE.Add(res(i)(1))
                    i += 1
                Loop Until i = res.Count
            Else
                TE.Add(0)
                PE.Add(0)
            End If

            Dim Pest, Tmax As Double, eos As String

            If Me.ComponentName.Contains("PR") Then eos = "PR" Else eos = "SRK"

            Pest = PCR * 10
            Tmin = MathEx.Common.Max(Me.RET_VTF)
            If Tmin = 0.0# Then Tmin = MathEx.Common.Min(Me.RET_VTB) * 0.4
            Tmax = TCR * 1.4

            If CBool(parameters(4)) = True Then
                If bw IsNot Nothing Then bw.ReportProgress(0, "Phase Identification Parameter")
                For T = Tmin To Tmax Step 5
                    TI.Add(T)
                    PI.Add(Auxiliary.FlashAlgorithms.FlashAlgorithm.CalcPIPressure(Vz, Pest, T, Me, eos))
                    Pest = PI(PI.Count - 1)
                Next
            Else
                TI.Add(0)
                PI.Add(0)
            End If

            If TVB.Count > 1 Then TVB.RemoveAt(TVB.Count - 1)
            If PB.Count > 1 Then PB.RemoveAt(PB.Count - 1)
            If HB.Count > 1 Then HB.RemoveAt(HB.Count - 1)
            If SB.Count > 1 Then SB.RemoveAt(SB.Count - 1)
            If VB.Count > 1 Then VB.RemoveAt(VB.Count - 1)
            If TVB.Count > 1 Then TVB.RemoveAt(TVB.Count - 1)
            If PB.Count > 1 Then PB.RemoveAt(PB.Count - 1)
            If HB.Count > 1 Then HB.RemoveAt(HB.Count - 1)
            If SB.Count > 1 Then SB.RemoveAt(SB.Count - 1)
            If VB.Count > 1 Then VB.RemoveAt(VB.Count - 1)

            If ToWF.Count > 1 Then ToWF.RemoveAt(ToWF.Count - 1)
            If PoWF.Count > 1 Then PoWF.RemoveAt(PoWF.Count - 1)
            If HoWF.Count > 1 Then HoWF.RemoveAt(HoWF.Count - 1)
            If SoWF.Count > 1 Then SoWF.RemoveAt(SoWF.Count - 1)
            If VoWF.Count > 1 Then VoWF.RemoveAt(VoWF.Count - 1)

            If TVD.Count > 1 Then TVD.RemoveAt(TVD.Count - 1)
            If PO.Count > 1 Then PO.RemoveAt(PO.Count - 1)
            If HO.Count > 1 Then HO.RemoveAt(HO.Count - 1)
            If SO.Count > 1 Then SO.RemoveAt(SO.Count - 1)
            If VO.Count > 1 Then VO.RemoveAt(VO.Count - 1)

            If TVD.Count > 1 Then TVD.RemoveAt(TVD.Count - 1)
            If PO.Count > 1 Then PO.RemoveAt(PO.Count - 1)
            If HO.Count > 1 Then HO.RemoveAt(HO.Count - 1)
            If SO.Count > 1 Then SO.RemoveAt(SO.Count - 1)
            If VO.Count > 1 Then VO.RemoveAt(VO.Count - 1)

            If TQ.Count > 1 Then TQ.RemoveAt(TQ.Count - 1)
            If PQ.Count > 1 Then PQ.RemoveAt(PQ.Count - 1)
            If TI.Count > 1 Then TI.RemoveAt(TI.Count - 1)
            If PI.Count > 1 Then PI.RemoveAt(PI.Count - 1)

            Return New Object() {TVB, PB, HB, SB, VB, TVD, PO, HO, SO, VO, TE, PE, TH, PHsI, PHsII, CP, TQ, PQ, TI, PI, ToWF, PoWF, HoWF, SoWF, VoWF}

        End Function

        ''' <summary>
        ''' This function returns points to build the binary phase envelope.
        ''' </summary>
        ''' <param name="parameters"></param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Overridable Function DW_ReturnBinaryEnvelope(ByVal parameters As Object, Optional ByVal bw As System.ComponentModel.BackgroundWorker = Nothing) As Object

            If Not Calculator.CAPEOPENMode Then
                Try
                    Me._tpseverity = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ThreePhaseFlashStabTestSeverity
                    Me._tpcompids = Me.CurrentMaterialStream.Flowsheet.FlowsheetOptions.ThreePhaseFlashStabTestCompIds
                Catch ex As Exception
                    Me._tpseverity = 0
                    Me._tpcompids = New String() {}
                Finally
                End Try
            End If

            If Calculator.EnableGPUProcessing Then Calculator.InitComputeDevice()

            Dim n, i As Integer

            n = Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1

            Dim dx As Double = 0.025

            Dim tipocalc As String
            Dim result As Object
            Dim P, T As Double
            Dim calcP, calcT As Double
            Dim VLE, SLE, LLE, Critical, SolidSolution As Boolean

            tipocalc = parameters(0)
            P = parameters(1)
            T = parameters(2)
            VLE = parameters(3)
            LLE = parameters(4)
            SLE = parameters(5)
            Critical = parameters(6)
            SolidSolution = parameters(7)

            Select Case tipocalc

                Case "T-x-y"

                    Dim px, py1, py2, px1l1, px1l2, py3 As New ArrayList 'vle, lle
                    Dim pxs1, pys1, pxs2, pys2 As New ArrayList 'sle
                    Dim pxc, pyc As New ArrayList 'critical
                    Dim unstable As Boolean = False
                    Dim ui, ut As New ArrayList
                    Dim x, y1, y2, Test1, Test2 As Double
                    Dim tmp1 As Object = Nothing, tmp2 As Object = Nothing

                    'If VLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then
                    If VLE Then
                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "VLE (" & i + 1 & "/41)")
                            Try
                                If i = 0 Then
                                    tmp1 = Me.FlashBase.Flash_PV(New Double() {i * dx, 1 - i * dx}, P, 0, 0, Me)
                                    calcT = tmp1(4)
                                    Test1 = calcT
                                    tmp2 = Me.FlashBase.Flash_PV(New Double() {i * dx, 1 - i * dx}, P, 1, 0, Me)
                                    y2 = tmp2(4)
                                    Test2 = y2
                                Else
                                    tmp1 = Me.FlashBase.Flash_PV(New Double() {i * dx, 1 - i * dx}, P, 0, Test1, Me)
                                    calcT = tmp1(4)
                                    Test1 = calcT
                                    tmp2 = Me.FlashBase.Flash_PV(New Double() {i * dx, 1 - i * dx}, P, 1, Test2, Me)
                                    y2 = tmp2(4)
                                    Test2 = y2
                                End If
                                x = i * dx
                                y1 = calcT
                                px.Add(x)
                                py1.Add(y1)
                                py2.Add(y2)

                                'check if liquid phase is stable.
                                If tmp1(7) > 0 Then
                                    unstable = True
                                    ui.Add(px.Count - 1)
                                    ut.Add(tmp1(4))
                                End If

                                'check if liquid phase is stable.
                                'Test2 = x * Me.RET_VTF()(0) + (1 - x) * Me.RET_VTF()(1)
                                'result = Me.FlashBase.Flash_PT(New Double() {i * dx, 1 - i * dx}, P, Test1 * 0.8, Me)
                                'If result(5) > 0.0# Then
                                '    If Abs(result(2)(0) - result(6)(0)) > 0.01 Then
                                '        unstable = True
                                '        ui.Add(px.Count - 1)
                                '        ut.Add(Me.FlashBase.BubbleTemperature_LLE(New Double() {i * dx, 1 - i * dx}, result(2), result(6), P, y1 - 50, y2 + 20, Me))
                                '        py1(py1.Count - 1) = ut(ut.count-1)
                                '    End If
                                'End If

                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1
                    End If

                    If LLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then

                        Select Case Me.FlashAlgorithm
                            Case FlashMethod.DWSIMDefault, FlashMethod.GibbsMin2P, FlashMethod.InsideOut, FlashMethod.NestedLoopsImmiscible, FlashMethod.NestedLoopsSLE
                                Throw New Exception(Calculator.GetLocalString("UnsuitableFlashAlgorithmSelected"))
                        End Select

                        If unstable Then
                            Dim ti, tf, uim, tit As Double
                            ti = (ut(0) + ut(ut.Count - 1)) / 2
                            uim = (ui(0) + ui(ui.Count - 1)) / 2
                            tf = MathEx.Common.Max(Me.RET_VTF())
                            If tf = 0.0# Then tf = ti * 0.7

                            For i = 0 To 25
                                tit = tf + (ti - tf) / 25 * i
                                If bw IsNot Nothing Then If bw.CancellationPending Then Exit For Else bw.ReportProgress(0, "LLE (" & i + 1 & "/26)")
                                result = Me.FlashBase.Flash_PT(New Double() {uim * dx, 1 - uim * dx}, P, tit, Me)
                                If result(5) > 0.0# Then
                                    If Abs(result(2)(0) - result(6)(0)) > 0.01 Then
                                        px1l1.Add(result(2)(0))
                                        px1l2.Add(result(6)(0))
                                        py3.Add(tit)
                                    End If
                                End If
                            Next
                        End If
                    End If

                    If SLE Then

                        Dim nlsle As New Auxiliary.FlashAlgorithms.NestedLoopsSLE
                        Dim L1, L2 As Double

                        If SolidSolution Then
                            L1 = 0.0#
                            L2 = 1.0#
                        Else
                            L1 = 0.001
                            L2 = 0.999
                        End If

                        nlsle.SolidSolution = SolidSolution

                        Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)
                        For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                            constprops.Add(su.ConstantProperties)
                        Next
                        nlsle.CompoundProperties = constprops

                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "SLE 1 (" & i + 1 & "/42)")
                            Try
                                tmp1 = nlsle.Flash_PSF(New Double() {i * dx, 1 - i * dx}, P, L1, 0, Me)
                                y1 = tmp1(4)
                                x = i * dx
                                pxs1.Add(x)
                                pys1.Add(y1)
                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1

                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "SLE 2 (" & i + 1 & "/42)")
                            Try
                                If i = 0 Then
                                    x = 0.001
                                ElseIf i * dx = 1 Then
                                    x = 0.999
                                Else
                                    x = i * dx
                                End If
                                tmp1 = nlsle.Flash_PSF(New Double() {x, 1 - x}, P, L2, 0, Me)
                                y2 = tmp1(4)
                                Test2 = y2
                                pxs2.Add(x)
                                pys2.Add(y2)
                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1
                    End If

                    If Critical Then

                        Dim cpc As New Utilities.TCP.Methods
                        Dim cpcs As New Utilities.TCP.Methods_SRK
                        Dim CP As New ArrayList
                        Dim TCR, PCR, VCR As Double

                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "Critical (" & i + 1 & "/42)")
                            Try
                                If TypeOf Me Is PengRobinsonPropertyPackage Then
                                    If i = 0 Or (i * dx) >= 1 Then
                                        Dim Tc As Double() = Me.RET_VTC
                                        TCR = (i * dx) * Tc(0) + (1 - i * dx) * Tc(1)
                                        PCR = 0.0#
                                        VCR = 0.0#
                                        CP.Clear()
                                        CP.Add(New Double() {TCR, PCR, VCR})
                                    Else
                                        CP = cpc.CRITPT_PR(New Double() {i * dx, 1 - i * dx}, Me.RET_VTC, Me.RET_VPC, Me.RET_VVC, Me.RET_VW, Me.RET_VKij)
                                    End If
                                ElseIf TypeOf Me Is SRKPropertyPackage Then
                                    If i = 0 Or (i * dx) >= 1 Then
                                        Dim Tc As Double() = Me.RET_VTC
                                        TCR = (i * dx) * Tc(0) + (1 - i * dx) * Tc(1)
                                        PCR = 0.0#
                                        VCR = 0.0#
                                        CP.Clear()
                                        CP.Add(New Double() {TCR, PCR, VCR})
                                    Else
                                        CP = cpcs.CRITPT_PR(New Double() {i * dx, 1 - i * dx}, Me.RET_VTC, Me.RET_VPC, Me.RET_VVC, Me.RET_VW, Me.RET_VKij)
                                    End If
                                Else
                                    Dim Tc As Double() = Me.RET_VTC
                                    TCR = (i * dx) * Tc(0) + (1 - i * dx) * Tc(1)
                                    PCR = 0.0#
                                    VCR = 0.0#
                                    CP.Clear()
                                    CP.Add(New Double() {TCR, PCR, VCR})
                                End If
                                If CP.Count > 0 Then
                                    Dim cp0 = CP(0)
                                    TCR = cp0(0)
                                    PCR = cp0(1)
                                    VCR = cp0(2)
                                    x = i * dx
                                    pxc.Add(x)
                                    pyc.Add(TCR)
                                End If
                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1
                    End If

                    Return New Object() {px, py1, py2, px1l1, px1l2, py3, pxs1, pys1, pxs2, pys2, pxc, pyc}

                Case "P-x-y"

                    Dim px, py1, py2, px1l1, px1l2, py3 As New ArrayList
                    Dim unstable As Boolean = False
                    Dim ui, up As New ArrayList
                    Dim x, y1, y2, Pest1, Pest2 As Double
                    Dim tmp As Object = Nothing

                    If Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then

                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "VLE (" & i + 1 & "/42)")
                            Try
                                If i = 0 Then
                                    tmp = Me.FlashBase.Flash_TV(New Double() {i * dx, 1 - i * dx}, T, 0, 0, Me)
                                    calcP = tmp(4)
                                    Pest1 = calcP
                                    tmp = Me.FlashBase.Flash_TV(New Double() {i * dx, 1 - i * dx}, T, 1, 0, Me)
                                    y2 = tmp(4)
                                    Pest2 = y2
                                Else
                                    tmp = Me.FlashBase.Flash_TV(New Double() {i * dx, 1 - i * dx}, T, 0, Pest1, Me)
                                    calcP = tmp(4)
                                    Pest1 = calcP
                                    tmp = Me.FlashBase.Flash_TV(New Double() {i * dx, 1 - i * dx}, T, 1, Pest2, Me)
                                    y2 = tmp(4)
                                    Pest2 = y2
                                End If
                                x = i * dx
                                y1 = calcP
                                px.Add(x)
                                py1.Add(y1)
                                py2.Add(y2)
                                'check if liquid phase is stable.
                                result = Me.FlashBase.Flash_PT(New Double() {i * dx, 1 - i * dx}, calcP * 1.3, T, Me)
                                If result(5) > 0.0# Then
                                    Dim fcl1(1), fcl2(1) As Double
                                    fcl1 = Me.DW_CalcFugCoeff(result(2), T, calcP, State.Liquid)
                                    calcP = result(2)(0) * fcl1(0) * calcP + result(2)(1) * fcl1(1) * calcP
                                    unstable = True
                                    ui.Add(px.Count - 1)
                                    'up.Add(calcP)
                                    If up.Count = 0 Then up.Add(calcP)
                                    py1(py1.Count - 1) = up(0)
                                End If
                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1

                        If unstable Then
                            Dim pi, pf, uim As Double, pit As Integer
                            pi = up(0)
                            uim = (ui(0) + ui(ui.Count - 1)) / 2
                            pf = 2 * pi
                            i = 0
                            For pit = pi To pf Step (pf - pi) / 10
                                If bw IsNot Nothing Then If bw.CancellationPending Then Exit For Else bw.ReportProgress(0, "LLE (" & i + 1 & "/28)")
                                result = Me.FlashBase.Flash_PT(New Double() {uim * dx, 1 - uim * dx}, pit, T, Me)
                                If result(5) > 0.0# Then
                                    If Abs(result(2)(0) - result(6)(0)) > 0.01 Then
                                        px1l1.Add(result(2)(0))
                                        px1l2.Add(result(6)(0))
                                        py3.Add(pit)
                                    End If
                                End If
                                i += 1
                            Next
                        End If

                    End If

                    Return New Object() {px, py1, py2, px1l1, px1l2, py3}

                Case "(T)x-y"

                    Dim px, py As New ArrayList
                    Dim Test1 As Double
                    Dim x, y As Double
                    Dim tmp As Object = Nothing

                    If Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then
                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "VLE (" & i + 1 & "/42)")
                            Try
                                If i = 0 Then
                                    tmp = Me.FlashBase.Flash_PV(New Double() {i * dx, 1 - i * dx}, P, 0, 0, Me)
                                    calcT = tmp(4)
                                    Test1 = calcT
                                Else
                                    tmp = Me.FlashBase.Flash_PV(New Double() {i * dx, 1 - i * dx}, P, 0, Test1, Me)
                                    calcT = tmp(4)
                                    Test1 = calcT
                                End If
                                x = i * dx
                                y = tmp(3)(0)
                                px.Add(x)
                                py.Add(y)
                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1
                    End If

                    Return New Object() {px, py}

                Case Else

                    Dim px, py As New ArrayList

                    Dim Pest1 As Double
                    Dim x, y As Double
                    Dim tmp As Object = Nothing

                    If Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE And Not Me.FlashAlgorithm = FlashMethod.NestedLoopsSLE_SS Then
                        i = 0
                        Do
                            If bw IsNot Nothing Then If bw.CancellationPending Then Exit Do Else bw.ReportProgress(0, "VLE (" & i + 1 & "/42)")
                            Try
                                If i = 0 Then
                                    tmp = Me.FlashBase.Flash_TV(New Double() {i * dx, 1 - i * dx}, T, 0, 0, Me)
                                    calcP = tmp(4)
                                    Pest1 = calcP
                                Else
                                    tmp = Me.FlashBase.Flash_TV(New Double() {i * dx, 1 - i * dx}, T, 0, Pest1, Me)
                                    calcP = tmp(4)
                                    Pest1 = calcP
                                End If
                                x = i * dx
                                y = tmp(3)(0)
                                px.Add(x)
                                py.Add(y)
                            Catch ex As Exception
                            End Try
                            i = i + 1
                        Loop Until (i - 1) * dx >= 1
                    End If

                    Return New Object() {px, py}

            End Select

        End Function

        Public MustOverride Sub DW_CalcCompPartialVolume(ByVal phase As PropertyPackages.Phase, ByVal T As Double, ByVal P As Double)

#End Region

#Region "   Commmon Functions"

        Public Function DW_GetConstantProperties() As List(Of Interfaces.ICompoundConstantProperties)

            Dim constprops As New List(Of Interfaces.ICompoundConstantProperties)

            For Each su As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                constprops.Add(su.ConstantProperties)
            Next

            Return constprops

        End Function

        Public Overloads Sub DW_CalcKvalue()

            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                subst.Kvalue = Me.CurrentMaterialStream.Phases(2).Compounds(subst.Name).MoleFraction.GetValueOrDefault / Me.CurrentMaterialStream.Phases(1).Compounds(subst.Name).MoleFraction.GetValueOrDefault
                subst.lnKvalue = Log(subst.Kvalue)
            Next

        End Sub

        Public Sub DW_CalcCompMolarFlow(ByVal phaseID As Integer)

            If Not phaseID = -1 Then
                With Me.CurrentMaterialStream.Phases(phaseID)
                    For Each subs As Interfaces.ICompound In .Compounds.Values
                        subs.MolarFlow = .Properties.molarflow.GetValueOrDefault * subs.MoleFraction.GetValueOrDefault
                    Next
                End With
            Else
                For Each phase In Me.CurrentMaterialStream.Phases.Values
                    With phase
                        For Each subs As Interfaces.ICompound In .Compounds.Values
                            subs.MolarFlow = .Properties.molarflow.GetValueOrDefault * subs.MoleFraction.GetValueOrDefault
                        Next
                    End With
                Next
            End If

        End Sub

        Public Overridable Sub DW_CalcCompFugCoeff(ByVal f As Phase)

            Dim fc As Object
            Dim vmol As Object = Me.RET_VMOL(f)
            Dim P, T As Double
            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault
            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
            Select Case f
                Case Phase.Vapor
                    fc = Me.DW_CalcFugCoeff(vmol, T, P, State.Vapor)
                Case Else
                    fc = Me.DW_CalcFugCoeff(vmol, T, P, State.Liquid)
            End Select
            Dim i As Integer = 0
            For Each subs As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(f)).Compounds.Values
                subs.FugacityCoeff = fc(i)
                subs.ActivityCoeff = fc(i) * P / Me.AUX_PVAPi(i, T)
                i += 1
            Next

        End Sub

        Public Sub DW_CalcCompMassFlow(ByVal phaseID As Integer)

            If Not phaseID = -1 Then
                With Me.CurrentMaterialStream.Phases(phaseID)
                    For Each subs As Interfaces.ICompound In .Compounds.Values
                        subs.MassFlow = .Properties.massflow.GetValueOrDefault * subs.MassFraction.GetValueOrDefault
                    Next
                End With
            Else
                For Each phase In Me.CurrentMaterialStream.Phases.Values
                    With phase
                        For Each subs As Interfaces.ICompound In .Compounds.Values
                            subs.MassFlow = .Properties.massflow.GetValueOrDefault * subs.MassFraction.GetValueOrDefault
                        Next
                    End With
                Next
            End If

        End Sub

        Public Sub DW_CalcCompVolFlow(ByVal phaseID As Integer)

            Dim TotalMolarFlow, TotalVolFlow, MolarFrac, PartialVol, VolFrac, VolFlow, Sum As Double

            Dim T, P As Double
            T = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault
            P = Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault
            Me.DW_CalcCompPartialVolume(Me.RET_PHASECODE(phaseID), T, P)

            If Not phaseID = -1 Then
                With Me.CurrentMaterialStream.Phases(phaseID)

                    Sum = 0

                    For Each subs As Interfaces.ICompound In .Compounds.Values
                        TotalMolarFlow = .Properties.molarflow.GetValueOrDefault
                        TotalVolFlow = .Properties.volumetric_flow.GetValueOrDefault
                        MolarFrac = subs.MoleFraction.GetValueOrDefault
                        PartialVol = subs.PartialVolume.GetValueOrDefault
                        VolFlow = TotalMolarFlow * MolarFrac * PartialVol
                        If TotalVolFlow > 0 Then
                            VolFrac = VolFlow / TotalVolFlow
                        Else
                            VolFrac = 0
                        End If
                        subs.VolumetricFraction = VolFrac
                        Sum += VolFrac
                    Next

                    'Normalization is still needed due to minor deviations in the partial molar volume estimation. Summation of partial flow rates
                    'should match phase flow rate.
                    For Each subs As Interfaces.ICompound In .Compounds.Values
                        If Sum > 0 Then
                            subs.VolumetricFraction = subs.VolumetricFraction.GetValueOrDefault / Sum
                        Else
                            subs.VolumetricFraction = 0
                        End If
                        'Corrects volumetric flow rate after normalization of fractions.
                        subs.VolumetricFlow = subs.VolumetricFraction.GetValueOrDefault * TotalVolFlow
                    Next

                End With
            Else
                For Each phase In Me.CurrentMaterialStream.Phases.Values
                    With phase

                        Sum = 0

                        For Each subs As Interfaces.ICompound In .Compounds.Values
                            TotalMolarFlow = .Properties.molarflow.GetValueOrDefault
                            TotalVolFlow = .Properties.volumetric_flow.GetValueOrDefault
                            MolarFrac = subs.MoleFraction.GetValueOrDefault
                            PartialVol = subs.PartialVolume.GetValueOrDefault
                            VolFlow = TotalMolarFlow * MolarFrac * PartialVol
                            If TotalVolFlow > 0 Then
                                VolFrac = VolFlow / TotalVolFlow
                            Else
                                VolFrac = 0
                            End If
                            subs.VolumetricFraction = VolFrac
                            Sum += VolFrac
                        Next

                        'Normalization is still needed due to minor deviations in the partial molar volume estimation. Summation of partial flow rates
                        'should match phase flow rate.
                        For Each subs As Interfaces.ICompound In .Compounds.Values
                            If Sum > 0 Then
                                subs.VolumetricFraction = subs.VolumetricFraction.GetValueOrDefault / Sum
                            Else
                                subs.VolumetricFraction = 0
                            End If
                            'Corrects volumetric flow rate after normalization of fractions.
                            subs.VolumetricFlow = subs.VolumetricFraction.GetValueOrDefault * TotalVolFlow
                        Next

                    End With
                Next
            End If

            'Totalization for the mixture "phase" should be made separatly, since the concept o partial molar volume is non sense for the whole mixture.
            For Each Subs As Interfaces.ICompound In CurrentMaterialStream.Phases(0).Compounds.Values
                Subs.VolumetricFraction = 0
                Subs.VolumetricFlow = 0
            Next

            'Summation of volumetric flow rates over all phases.
            For APhaseID As Integer = 1 To Me.CurrentMaterialStream.Phases.Count - 1
                For Each Subs As String In CurrentMaterialStream.Phases(APhaseID).Compounds.Keys
                    CurrentMaterialStream.Phases(0).Compounds(Subs).VolumetricFlow = CurrentMaterialStream.Phases(0).Compounds(Subs).VolumetricFlow.GetValueOrDefault + CurrentMaterialStream.Phases(APhaseID).Compounds(Subs).VolumetricFlow.GetValueOrDefault
                Next
            Next

            'Calculate volumetric fractions for the mixture.

            For Each Subs As Interfaces.ICompound In CurrentMaterialStream.Phases(0).Compounds.Values
                If CurrentMaterialStream.Phases(0).Properties.volumetric_flow.GetValueOrDefault > 0 Then
                    Subs.VolumetricFraction = Subs.VolumetricFlow.GetValueOrDefault / CurrentMaterialStream.Phases(0).Properties.volumetric_flow.GetValueOrDefault
                Else
                    Subs.VolumetricFraction = 0
                End If
            Next

        End Sub

        Public Sub DW_ZerarPhaseProps(ByVal Phase As Phase)

            Dim phaseID As Integer

            phaseID = Me.RET_PHASEID(Phase)

            If Me.CurrentMaterialStream.Phases.ContainsKey(phaseID) Then

                If phaseID <> 0 Then

                    Me.CurrentMaterialStream.Phases(phaseID).Properties.density = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.enthalpy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.entropy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_enthalpy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_entropy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.enthalpyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.entropyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_enthalpyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_entropyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.compressibilityFactor = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.heatCapacityCp = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.heatCapacityCv = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molecularWeight = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.thermalConductivity = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.speedOfSound = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.volumetric_flow = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.jouleThomsonCoefficient = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.excessEnthalpy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.excessEntropy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.compressibility = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.bubbleTemperature = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.bubblePressure = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.dewTemperature = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.dewPressure = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.viscosity = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.kinematic_viscosity = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molarflow = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.massflow = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.massfraction = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molarfraction = Nothing

                Else

                    Me.CurrentMaterialStream.Phases(phaseID).Properties.density = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.enthalpy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.entropy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_enthalpy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_entropy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.enthalpyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.entropyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_enthalpyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molar_entropyF = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.compressibilityFactor = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.heatCapacityCp = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.heatCapacityCv = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molecularWeight = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.thermalConductivity = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.speedOfSound = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.volumetric_flow = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.jouleThomsonCoefficient = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.excessEnthalpy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.excessEntropy = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.compressibility = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.bubbleTemperature = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.bubblePressure = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.dewTemperature = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.dewPressure = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.viscosity = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.kinematic_viscosity = Nothing

                    Me.CurrentMaterialStream.Phases(phaseID).Properties.molarflow = Nothing
                    Me.CurrentMaterialStream.Phases(phaseID).Properties.massflow = Nothing

                End If

            End If

        End Sub

        Public Sub DW_ZerarTwoPhaseProps(ByVal Phase1 As Phase, ByVal Phase2 As Phase)

            Me.CurrentMaterialStream.Phases(0).Properties.kvalue = Nothing
            Me.CurrentMaterialStream.Phases(0).Properties.logKvalue = Nothing
            Me.CurrentMaterialStream.Phases(0).Properties.surfaceTension = Nothing

        End Sub

        Public Sub DW_ZerarOverallProps()

        End Sub

        Public Sub DW_ZerarVazaoMolar()
            With Me.CurrentMaterialStream
                .Phases(0).Properties.molarflow = 0
            End With
        End Sub

        Public Sub DW_ZerarVazaoVolumetrica()
            With Me.CurrentMaterialStream
                .Phases(0).Properties.volumetric_flow = 0
            End With
        End Sub

        Public Sub DW_ZerarVazaoMassica()
            With Me.CurrentMaterialStream
                .Phases(0).Properties.massflow = 0
            End With
        End Sub

        Public Sub DW_ZerarComposicoes(ByVal Phase As Phase)

            Dim phaseID As Integer
            phaseID = Me.RET_PHASEID(Phase)

            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(phaseID).Compounds.Values
                subst.MoleFraction = Nothing
                subst.MassFraction = Nothing
                subst.MolarFlow = Nothing
                subst.MassFlow = Nothing
            Next

        End Sub

        Public Function AUX_TCM(ByVal Phase As Phase) As Double

            Dim Tc As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                Tc += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Critical_Temperature
            Next

            Return Tc

        End Function

        Public Function AUX_TBM(ByVal Phase As Phase) As Double

            Dim Tb As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                Tb += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Normal_Boiling_Point
            Next

            Return Tb

        End Function

        Public Function AUX_TFM(ByVal Phase As Phase) As Double

            Dim Tf As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                Tf += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.TemperatureOfFusion
            Next

            Return Tf

        End Function

        Public Function AUX_WM(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Acentric_Factor
            Next

            Return val

        End Function

        Public Function AUX_ZCM(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Critical_Compressibility
            Next

            Return val

        End Function

        Public Function AUX_ZRAM(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                If subst.ConstantProperties.Z_Rackett <> 0 Then
                    val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Z_Rackett
                Else
                    val += subst.MoleFraction.GetValueOrDefault * (0.29056 - 0.08775 * subst.ConstantProperties.Acentric_Factor)
                End If
            Next

            Return val

        End Function

        Public Function AUX_VCM(ByVal Phase As Phase) As Double

            Dim val, vc As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                vc = subst.ConstantProperties.Critical_Volume
                If vc = 0.0# Then
                    vc = Auxiliary.PROPS.Vc(subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor)
                End If
                val += subst.MoleFraction.GetValueOrDefault * vc
            Next

            Return val / 1000

        End Function

        Public Function AUX_PCM(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Critical_Pressure
            Next

            Return val

        End Function

        Public Function AUX_KHenry(ByVal CompName As String, ByVal T As Double) As Double

            Dim KHx As Double
            Dim MW As Double = 18 'mol weight of water [g/mol]
            Dim DW As Double = 996 'density of water at 298.15 K [Kg/m3]
            Dim KHCP As Double = 0.0000064 'nitrogen [mol/m3/Pa]
            Dim C As Double = 1600 'nitrogen
            Dim CAS As String

            CAS = Me.CurrentMaterialStream.Phases(0).Compounds(CompName).ConstantProperties.CAS_Number

            If m_Henry.ContainsKey(CAS) Then
                KHCP = m_Henry(CAS).KHcp
                C = m_Henry(CAS).C
            End If
            KHx = 1 / (KHCP * MW / DW / 1000 * Exp(C * (1 / T - 1 / 298.15)))

            Return KHx '[Pa]

        End Function

        Public Function AUX_PVAPM(ByVal T) As Double

            Dim val As Double = 0
            Dim subst As Interfaces.ICompound
            Dim Tc As Double

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                Tc = subst.ConstantProperties.Critical_Temperature
                If T / Tc <= 1 Then
                    val += subst.MoleFraction.GetValueOrDefault * Me.AUX_PVAPi(subst.Name, T)
                Else
                    val += subst.MoleFraction.GetValueOrDefault * Me.AUX_PVAPi(subst.Name, 0.5 * Tc)
                End If
            Next

            Return val

        End Function

        Public Function AUX_KIJ(ByVal sub1 As String, ByVal sub2 As String) As Double

            Dim Vc1 As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Volume
            Dim Vc2 As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub2).ConstantProperties.Critical_Volume

            Dim tmp As Double = 1 - 8 * (Vc1 * Vc2) ^ 0.5 / ((Vc1 ^ (1 / 3) + Vc2 ^ (1 / 3)) ^ 3)

            Return tmp

            Return 0

        End Function

        Public Overridable Function AUX_MMM(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.Molar_Weight
            Next

            Return val

        End Function

        Private Function AUX_Rackett_PHIi(ByVal sub1 As String, ByVal Phase As Phase) As Double

            Dim val, vc As Double
            vc = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Volume
            If vc = 0.0# Then vc = Auxiliary.PROPS.Vc(Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds(sub1).ConstantProperties.Critical_Temperature, Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds(sub1).ConstantProperties.Critical_Pressure, Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds(sub1).ConstantProperties.Acentric_Factor)

            val = Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds(sub1).MoleFraction.GetValueOrDefault * vc

            val = val / Me.AUX_VCM(Phase) / 1000

            Return val

        End Function

        Private Function AUX_Rackett_Kij(ByVal sub1 As String, ByVal sub2 As String) As Double

            Dim Vc1 As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Volume
            Dim Vc2 As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub2).ConstantProperties.Critical_Volume

            If Vc1 = 0.0# Then Vc1 = Auxiliary.PROPS.Vc(Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature, Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Pressure, Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Acentric_Factor)
            If Vc2 = 0.0# Then Vc2 = Auxiliary.PROPS.Vc(Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature, Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Pressure, Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Acentric_Factor)

            Dim tmp As Double = 8 * (Vc1 * Vc2) ^ 0.5 / ((Vc1 ^ (1 / 3) + Vc2 ^ (1 / 3)) ^ 3)

            Return tmp

        End Function

        Private Function AUX_Rackett_Tcij(ByVal sub1 As String, ByVal sub2 As String) As Double

            Dim Tc1 As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature
            Dim Tc2 As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub2).ConstantProperties.Critical_Temperature

            Dim tmp As Double = Me.AUX_Rackett_Kij(sub1, sub2) * (Tc1 * Tc2) ^ 0.5

            Return tmp

        End Function

        Private Function AUX_Rackett_Tcm(ByVal Phase As Phase) As Double

            Dim Tc As Double
            Dim subst1, subst2 As Interfaces.ICompound

            For Each subst1 In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                For Each subst2 In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                    Tc += Me.AUX_Rackett_PHIi(subst1.Name, Phase) * Me.AUX_Rackett_PHIi(subst2.Name, Phase) * Me.AUX_Rackett_Tcij(subst1.Name, subst2.Name)
                Next
            Next

            Return Tc

        End Function

        Public Overridable Function AUX_CPi(ByVal sub1 As String, ByVal T As Double) As Double

            If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IsPF = 1 Then

                With Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties

                    Return Auxiliary.PROPS.Cpig_lk(.PF_Watson_K, .Acentric_Factor, T) '* .Molar_Weight

                End With

            Else

                If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "DWSIM" Or _
                Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "" Then
                    Dim A, B, C, D, E, result As Double
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_E
                    'Cp = A + B*T + C*T^2 + D*T^3 + E*T^4 where Cp in kJ/kg-mol , T in K 
                    result = A + B * T + C * T ^ 2 + D * T ^ 3 + E * T ^ 4
                    Return result / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight 'kJ/kg.K
                ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "CheResources" Then
                    Dim A, B, C, D, E, result As Double
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_E
                    'CAL/MOL.K [CP=A+(B*T)+(C*T^2)+(D*T^3)], T in K
                    result = A + B * T + C * T ^ 2 + D * T ^ 3
                    Return result / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight * 4.1868 'kJ/kg.K
                ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "ChemSep" Or _
                Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "User" Then
                    Dim A, B, C, D, E, result As Double
                    Dim eqno As String = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IdealgasCpEquation
                    Dim mw As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_E
                    '<rppc name="Ideal gas heat capacity (RPP)"  units="J/kmol/K" >
                    result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) / 1000 / mw 'kJ/kg.K
                    Return result
                ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "Biodiesel" Then
                    Dim A, B, C, D, E, result As Double
                    Dim eqno As String = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IdealgasCpEquation
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Ideal_Gas_Heat_Capacity_Const_E
                    result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'kJ/kg.K
                    Return result
                Else
                    Return 0
                End If

            End If

        End Function

        Public Function AUX_CPm(ByVal Phase As Phase, ByVal T As Double) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MassFraction.GetValueOrDefault * Me.AUX_CPi(subst.Name, T)
            Next

            Return val 'KJ/Kg/K

        End Function

        Public Overridable Function AUX_HFm25(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.IG_Enthalpy_of_Formation_25C * subst.ConstantProperties.Molar_Weight
            Next

            Dim mw As Double = Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Properties.molecularWeight.GetValueOrDefault

            If mw <> 0.0# Then Return val / mw Else Return 0.0#

        End Function

        Public Overridable Function AUX_SFm25(ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * subst.ConstantProperties.IG_Entropy_of_Formation_25C * subst.ConstantProperties.Molar_Weight
            Next

            Dim mw As Double = Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Properties.molecularWeight.GetValueOrDefault

            If mw <> 0.0# Then Return val / mw Else Return 0.0#

        End Function

        Public Overridable Function AUX_PVAPi(ByVal sub1 As String, ByVal T As Double)

            If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IsPF = 1 Then

                With Me.CurrentMaterialStream.Phases(0).Compounds(sub1)

                    Return Auxiliary.PROPS.Pvp_leekesler(T, .ConstantProperties.Critical_Temperature, .ConstantProperties.Critical_Pressure, .ConstantProperties.Acentric_Factor)

                End With

            Else


                If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "DWSIM" Or _
                Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "" Then
                    Dim A, B, C, D, E, result As Double
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_E
                    result = Math.Exp(A + B / T + C * Math.Log(T) + D * T ^ E)
                    Return result
                ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "CheResources" Then
                    Dim A, B, C, result As Double
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_C
                    '[LN(P)=A-B/(T+C), P(mmHG) T(K)]
                    result = Math.Exp(A - B / (T + C)) * 133.322368 'mmHg to Pascal
                    Return result
                ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "ChemSep" Or _
                Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "User" Then
                    Dim A, B, C, D, E, result As Double
                    Dim eqno As String = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.VaporPressureEquation
                    Dim mw As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_E
                    '<vp_c name="Vapour pressure"  units="Pa" >
                    result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'Pa
                    If eqno = "0" Then
                        With Me.CurrentMaterialStream.Phases(0).Compounds(sub1)
                            result = Auxiliary.PROPS.Pvp_leekesler(T, .ConstantProperties.Critical_Temperature, .ConstantProperties.Critical_Pressure, .ConstantProperties.Acentric_Factor)
                        End With
                    End If
                    Return result
                ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "Biodiesel" Then
                    Dim A, B, C, D, E, result As Double
                    Dim eqno As String = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.VaporPressureEquation
                    A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_A
                    B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_B
                    C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_C
                    D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_D
                    E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Vapor_Pressure_Constant_E
                    result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'kPa
                    Return result * 1000
                Else
                    With Me.CurrentMaterialStream.Phases(0).Compounds(sub1)
                        Return Auxiliary.PROPS.Pvp_leekesler(T, .ConstantProperties.Critical_Temperature, .ConstantProperties.Critical_Pressure, .ConstantProperties.Acentric_Factor)
                    End With
                End If

            End If

        End Function

        Public Overridable Function AUX_PVAPi(ByVal index As Integer, ByVal T As Double) As Double

            Dim subst As Interfaces.ICompound
            Dim nome As String = ""
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                If i = index Then nome = subst.Name
                i += 1
            Next

            Return AUX_PVAPi(nome, T)

        End Function

        Public Overridable Function AUX_TSATi(ByVal PVAP As Double, ByVal subst As String) As Double

            Dim i As Integer

            Dim Tinf, Tsup As Double

            Dim fT, fT_inf, nsub, delta_T As Double

            Tinf = 10
            Tsup = 2000

            nsub = 25

            delta_T = (Tsup - Tinf) / nsub

            i = 0
            Do
                i = i + 1
                fT = PVAP - Me.AUX_PVAPi(subst, Tinf)
                Tinf = Tinf + delta_T
                fT_inf = PVAP - Me.AUX_PVAPi(subst, Tinf)
            Loop Until fT * fT_inf < 0 Or fT_inf > fT Or i >= 100
            If i = 100 Then Return 0.0#
            Tsup = Tinf
            Tinf = Tinf - delta_T

            'metodo de Brent para encontrar Vc

            Dim aaa, bbb, ccc, ddd, eee, min11, min22, faa, fbb, fcc, ppp, qqq, rrr, sss, tol11, xmm As Double
            Dim ITMAX2 As Integer = 100
            Dim iter2 As Integer

            aaa = Tinf
            bbb = Tsup
            ccc = Tsup

            faa = PVAP - Me.AUX_PVAPi(subst, aaa)
            fbb = PVAP - Me.AUX_PVAPi(subst, bbb)
            fcc = fbb

            iter2 = 0
            Do
                If (fbb > 0 And fcc > 0) Or (fbb < 0 And fcc < 0) Then
                    ccc = aaa
                    fcc = faa
                    ddd = bbb - aaa
                    eee = ddd
                End If
                If Math.Abs(fcc) < Math.Abs(fbb) Then
                    aaa = bbb
                    bbb = ccc
                    ccc = aaa
                    faa = fbb
                    fbb = fcc
                    fcc = faa
                End If
                tol11 = 0.0001
                xmm = 0.5 * (ccc - bbb)
                If (Math.Abs(xmm) <= tol11) Or (fbb = 0) Then GoTo Final3
                If (Math.Abs(eee) >= tol11) And (Math.Abs(faa) > Math.Abs(fbb)) Then
                    sss = fbb / faa
                    If aaa = ccc Then
                        ppp = 2 * xmm * sss
                        qqq = 1 - sss
                    Else
                        qqq = faa / fcc
                        rrr = fbb / fcc
                        ppp = sss * (2 * xmm * qqq * (qqq - rrr) - (bbb - aaa) * (rrr - 1))
                        qqq = (qqq - 1) * (rrr - 1) * (sss - 1)
                    End If
                    If ppp > 0 Then qqq = -qqq
                    ppp = Math.Abs(ppp)
                    min11 = 3 * xmm * qqq - Math.Abs(tol11 * qqq)
                    min22 = Math.Abs(eee * qqq)
                    Dim tvar2 As Double
                    If min11 < min22 Then tvar2 = min11
                    If min11 > min22 Then tvar2 = min22
                    If 2 * ppp < tvar2 Then
                        eee = ddd
                        ddd = ppp / qqq
                    Else
                        ddd = xmm
                        eee = ddd
                    End If
                Else
                    ddd = xmm
                    eee = ddd
                End If
                aaa = bbb
                faa = fbb
                If (Math.Abs(ddd) > tol11) Then
                    bbb += ddd
                Else
                    bbb += Math.Sign(xmm) * tol11
                End If
                fbb = PVAP - Me.AUX_PVAPi(subst, bbb)
                iter2 += 1
            Loop Until iter2 = ITMAX2

Final3:

            Return bbb

        End Function

        Public Overridable Function AUX_TSATi(ByVal PVAP As Double, ByVal index As Integer) As Double

            Dim i As Integer

            Dim Tinf, Tsup As Double

            Dim fT, fT_inf, nsub, delta_T As Double

            Tinf = 10
            Tsup = 2000

            nsub = 25

            delta_T = (Tsup - Tinf) / nsub

            i = 0
            Do
                i = i + 1
                fT = PVAP - Me.AUX_PVAPi(index, Tinf)
                Tinf = Tinf + delta_T
                fT_inf = PVAP - Me.AUX_PVAPi(index, Tinf)
            Loop Until fT * fT_inf < 0 Or fT_inf > fT Or i >= 100
            Tsup = Tinf
            Tinf = Tinf - delta_T

            'metodo de Brent para encontrar Vc

            Dim aaa, bbb, ccc, ddd, eee, min11, min22, faa, fbb, fcc, ppp, qqq, rrr, sss, tol11, xmm As Double
            Dim ITMAX2 As Integer = 100
            Dim iter2 As Integer

            aaa = Tinf
            bbb = Tsup
            ccc = Tsup

            faa = PVAP - Me.AUX_PVAPi(index, aaa)
            fbb = PVAP - Me.AUX_PVAPi(index, bbb)
            fcc = fbb

            iter2 = 0
            Do
                If (fbb > 0 And fcc > 0) Or (fbb < 0 And fcc < 0) Then
                    ccc = aaa
                    fcc = faa
                    ddd = bbb - aaa
                    eee = ddd
                End If
                If Math.Abs(fcc) < Math.Abs(fbb) Then
                    aaa = bbb
                    bbb = ccc
                    ccc = aaa
                    faa = fbb
                    fbb = fcc
                    fcc = faa
                End If
                tol11 = 0.0001
                xmm = 0.5 * (ccc - bbb)
                If (Math.Abs(xmm) <= tol11) Or (fbb = 0) Then GoTo Final3
                If (Math.Abs(eee) >= tol11) And (Math.Abs(faa) > Math.Abs(fbb)) Then
                    sss = fbb / faa
                    If aaa = ccc Then
                        ppp = 2 * xmm * sss
                        qqq = 1 - sss
                    Else
                        qqq = faa / fcc
                        rrr = fbb / fcc
                        ppp = sss * (2 * xmm * qqq * (qqq - rrr) - (bbb - aaa) * (rrr - 1))
                        qqq = (qqq - 1) * (rrr - 1) * (sss - 1)
                    End If
                    If ppp > 0 Then qqq = -qqq
                    ppp = Math.Abs(ppp)
                    min11 = 3 * xmm * qqq - Math.Abs(tol11 * qqq)
                    min22 = Math.Abs(eee * qqq)
                    Dim tvar2 As Double
                    If min11 < min22 Then tvar2 = min11
                    If min11 > min22 Then tvar2 = min22
                    If 2 * ppp < tvar2 Then
                        eee = ddd
                        ddd = ppp / qqq
                    Else
                        ddd = xmm
                        eee = ddd
                    End If
                Else
                    ddd = xmm
                    eee = ddd
                End If
                aaa = bbb
                faa = fbb
                If (Math.Abs(ddd) > tol11) Then
                    bbb += ddd
                Else
                    bbb += Math.Sign(xmm) * tol11
                End If
                fbb = PVAP - Me.AUX_PVAPi(index, bbb)
                iter2 += 1
            Loop Until iter2 = ITMAX2

Final3:

            Return bbb

        End Function

        Public Function RET_HVAPM(ByVal Vxw As Array, ByVal T As Double) As Double

            Dim val As Double = 0.0#
            Dim i As Integer
            Dim n As Integer = Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1

            i = 0
            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val += Vxw(i) * Me.AUX_HVAPi(subst.Name, T)
                i += 1
            Next

            Return val

        End Function

        Public Function AUX_HVAPi(ByVal sub1 As String, ByVal T As Double)

            Dim A, B, C, D, E, Tr, result As Double
            A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_A
            B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_B
            C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_C
            D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_D
            E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_E

            Tr = T / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature

            If Tr >= 1 Then Return 0.0#

            If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "DWSIM" Or _
                                Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "" Then
                If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IsHYPO = 1 Or _
                Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IsPF = 1 Then
                    Dim tr1 As Double
                    tr1 = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Normal_Boiling_Point / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature
                    result = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_A * ((1 - Tr) / (1 - tr1)) ^ 0.375
                    Return result 'kJ/kg
                Else
                    result = A * (1 - Tr) ^ (B + C * Tr + D * Tr ^ 2)
                    Return result / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight / 1000 'kJ/kg
                End If
            ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "CheResources" Or _
            Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "User" Then
                Dim tr1 As Double
                tr1 = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Normal_Boiling_Point / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature
                If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_A = 0.0# Then
                    Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_A = New Utilities.Hypos.Methods.HYP().DHvb_Vetere(Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature, Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Pressure, Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Normal_Boiling_Point)
                    Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_A /= Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight
                End If
                result = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.HVap_A * ((1 - Tr) / (1 - tr1)) ^ 0.375
                Return result 'kJ/kg
            ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "ChemSep" Then
                Dim eqno As String = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.VaporizationEnthalpyEquation
                result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, T / Tr) / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight / 1000 'kJ/kg
                Return result
            Else
                Return 0.0#
            End If

        End Function

        Public Overridable Function AUX_LIQVISCi(ByVal sub1 As String, ByVal T As Double)

            If T / Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature < 1 Then

                If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.IsPF = 1 Then

                    With Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties

                        Return Auxiliary.PROPS.oilvisc_twu(T, .PF_Tv1, .PF_Tv2, .PF_v1, .PF_v2) * Auxiliary.PROPS.liq_dens_rackett(T, .Critical_Temperature, .Critical_Pressure, .Acentric_Factor, .Molar_Weight, .Z_Rackett)

                    End With

                Else
                    If Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "DWSIM" Or _
                    Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "" Then
                        Dim A, B, C, D, E, result As Double
                        A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_A
                        B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_B
                        C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_C
                        D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_D
                        E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_E
                        result = Math.Exp(A + B / T + C * Math.Log(T) + D * T ^ E)
                        Return result
                    ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "CheResources" Then
                        Dim B, C, result As Double
                        B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_B
                        C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_C
                        '[LOG(V)=B*(1/T-1/C), T(K) V(CP)]
                        result = Exp(B * (1 / T - 1 / C)) * 0.001
                        Return result
                    ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "ChemSep" Or _
                    Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "User" Then
                        Dim A, B, C, D, E, result As Double
                        Dim eqno As String = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.LiquidViscosityEquation
                        Dim mw As Double = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight
                        A = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_A
                        B = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_B
                        C = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_C
                        D = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_D
                        E = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Liquid_Viscosity_Const_E
                        '<lvsc name="Liquid viscosity"  units="Pa.s" >
                        result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'Pa.s
                        If eqno = "0" Or eqno = "" Then
                            Dim Tc, Pc, w As Double
                            Tc = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature
                            Pc = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Pressure
                            w = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Acentric_Factor
                            mw = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight
                            result = Auxiliary.PROPS.viscl_letsti(T, Tc, Pc, w, mw)
                        End If
                        Return result
                    ElseIf Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.OriginalDB = "Biodiesel" Then
                        Dim result As Double
                        Dim Tc, Pc, w, Mw As Double
                        Tc = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Temperature
                        Pc = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Critical_Pressure
                        w = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Acentric_Factor
                        Mw = Me.CurrentMaterialStream.Phases(0).Compounds(sub1).ConstantProperties.Molar_Weight
                        result = Auxiliary.PROPS.viscl_letsti(T, Tc, Pc, w, Mw)
                        Return result
                    Else
                        Return 0
                    End If

                End If

            Else

                Return 0

            End If

        End Function

        Public Function AUX_LIQVISCm(ByVal T As Double, Optional ByVal phaseid As Integer = 3) As Double

            Dim val, val2, logvisc, result As Double
            Dim subst As Interfaces.ICompound

            val = 0
            val2 = 0
            For Each subst In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                'logvisc = Math.Log(Me.AUX_LIQVISCi(subst.Name, T))
                If Not Double.IsInfinity(logvisc) Then
                    val += subst.MoleFraction.GetValueOrDefault * Me.AUX_LIQVISCi(subst.Name, T)
                Else
                    val2 += subst.MoleFraction.GetValueOrDefault
                End If
            Next

            result = (val / (1 - val2))

            Return result

        End Function

        Public Overridable Function AUX_SURFTM(ByVal T As Double) As Double

            Dim val As Double = 0
            Dim tmpval As Double = 0.0#
            Dim nbp As Double
            Dim subst As Interfaces.ICompound
            Dim ftotal As Double = 1

            For Each subst In Me.CurrentMaterialStream.Phases(1).Compounds.Values
                If T / subst.ConstantProperties.Critical_Temperature < 1 Then
                    With subst.ConstantProperties
                        If .SurfaceTensionEquation <> "" And .SurfaceTensionEquation <> "0" And Not .IsIon And Not .IsSalt Then
                            tmpval = Me.CalcCSTDepProp(.SurfaceTensionEquation, .Surface_Tension_Const_A, .Surface_Tension_Const_B, .Surface_Tension_Const_C, .Surface_Tension_Const_D, .Surface_Tension_Const_E, T, .Critical_Temperature)
                        ElseIf .IsIon Or .IsSalt Then
                            tmpval = 0.0#
                        Else
                            nbp = subst.ConstantProperties.Normal_Boiling_Point
                            If nbp = 0 Then nbp = 0.7 * subst.ConstantProperties.Critical_Temperature
                            tmpval = Auxiliary.PROPS.sigma_bb(T, nbp, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure)
                        End If
                    End With
                Else
                    tmpval = 0
                    ftotal -= subst.MoleFraction.GetValueOrDefault
                End If
                val += subst.MoleFraction.GetValueOrDefault * tmpval / ftotal
            Next

            Return val

        End Function

        Public Overridable Function AUX_SURFTi(ByVal constprop As Interfaces.ICompoundConstantProperties, ByVal T As Double) As Double



            Dim val As Double = 0
            Dim nbp As Double
            Dim ftotal As Double = 1

            If T / constprop.Critical_Temperature < 1 Then
                With constprop
                    If .SurfaceTensionEquation <> "" And .SurfaceTensionEquation <> "0" And Not .IsIon And Not .IsSalt Then
                        val = Me.CalcCSTDepProp(.SurfaceTensionEquation, .Surface_Tension_Const_A, .Surface_Tension_Const_B, .Surface_Tension_Const_C, .Surface_Tension_Const_D, .Surface_Tension_Const_E, T, .Critical_Temperature)
                    ElseIf .IsIon Or .IsSalt Then
                        val = 0.0#
                    Else
                        nbp = constprop.Normal_Boiling_Point
                        If nbp = 0 Then nbp = 0.7 * constprop.Critical_Temperature
                        val = Auxiliary.PROPS.sigma_bb(T, nbp, constprop.Critical_Temperature, constprop.Critical_Pressure)
                    End If
                End With
            Else
            End If

            Return val

        End Function

        Public Overridable Function AUX_CONDTL(ByVal T As Double, Optional ByVal phaseid As Integer = 3) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound
            Dim vcl(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1)
            Dim i As Integer = 0
            For Each subst In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                If subst.ConstantProperties.LiquidThermalConductivityEquation <> "" Then
                    vcl(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidThermalConductivityEquation, subst.ConstantProperties.Liquid_Thermal_Conductivity_Const_A, subst.ConstantProperties.Liquid_Thermal_Conductivity_Const_B, subst.ConstantProperties.Liquid_Thermal_Conductivity_Const_C, subst.ConstantProperties.Liquid_Thermal_Conductivity_Const_D, subst.ConstantProperties.Liquid_Thermal_Conductivity_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                ElseIf subst.ConstantProperties.IsIon Or subst.ConstantProperties.IsSalt Then
                    vcl(i) = 0.0#
                Else
                    vcl(i) = Auxiliary.PROPS.condl_latini(T, subst.ConstantProperties.Normal_Boiling_Point, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Molar_Weight, "")
                End If

                i = i + 1
            Next
            val = Auxiliary.PROPS.condlm_li(Me.RET_VVC, vcl, Me.RET_VMOL(Me.RET_PHASECODE(phaseid)))
            Return val

        End Function

        Public Overridable Function AUX_CONDTG(ByVal T As Double, ByVal P As Double) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0
            For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                If subst.ConstantProperties.VaporThermalConductivityEquation <> "" Then
                    val += subst.MoleFraction.GetValueOrDefault * Me.CalcCSTDepProp(subst.ConstantProperties.VaporThermalConductivityEquation, subst.ConstantProperties.Vapor_Thermal_Conductivity_Const_A, subst.ConstantProperties.Vapor_Thermal_Conductivity_Const_B, subst.ConstantProperties.Vapor_Thermal_Conductivity_Const_C, subst.ConstantProperties.Vapor_Thermal_Conductivity_Const_D, subst.ConstantProperties.Vapor_Thermal_Conductivity_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                Else
                    val += subst.MoleFraction.GetValueOrDefault * Auxiliary.PROPS.condtg_elyhanley(T, Me.AUX_TCM(Phase.Vapor), Me.AUX_VCM(Phase.Vapor), Me.AUX_ZCM(Phase.Vapor), Me.AUX_WM(Phase.Vapor), Me.AUX_MMM(Phase.Vapor), Me.DW_CalcCv_ISOL(Phase.Vapor, T, P) * Me.AUX_MMM(Phase.Vapor))
                End If
                i = i + 1
            Next

            Return val

        End Function

        Public Overridable Function AUX_LIQTHERMCONDi(ByVal cprop As Interfaces.ICompoundConstantProperties, ByVal T As Double) As Double

            Dim val As Double

            If cprop.LiquidThermalConductivityEquation <> "" And cprop.LiquidThermalConductivityEquation <> "0" And Not cprop.IsIon And Not cprop.IsSalt Then
                val = Me.CalcCSTDepProp(cprop.LiquidThermalConductivityEquation, cprop.Liquid_Thermal_Conductivity_Const_A, cprop.Liquid_Thermal_Conductivity_Const_B, cprop.Liquid_Thermal_Conductivity_Const_C, cprop.Liquid_Thermal_Conductivity_Const_D, cprop.Liquid_Thermal_Conductivity_Const_E, T, cprop.Critical_Temperature)
            ElseIf cprop.IsIon Or cprop.IsSalt Then
                val = 0.0#
            Else
                val = Auxiliary.PROPS.condl_latini(T, cprop.Normal_Boiling_Point, cprop.Critical_Temperature, cprop.Molar_Weight, "")
            End If

            Return val

        End Function

        Public Overridable Function AUX_VAPTHERMCONDi(ByVal cprop As Interfaces.ICompoundConstantProperties, ByVal T As Double, ByVal P As Double) As Double

            Dim val As Double

            If cprop.VaporThermalConductivityEquation <> "" And cprop.VaporThermalConductivityEquation <> "0" And Not cprop.IsIon And Not cprop.IsSalt Then
                val = Me.CalcCSTDepProp(cprop.VaporThermalConductivityEquation, cprop.Vapor_Thermal_Conductivity_Const_A, cprop.Vapor_Thermal_Conductivity_Const_B, cprop.Vapor_Thermal_Conductivity_Const_C, cprop.Vapor_Thermal_Conductivity_Const_D, cprop.Vapor_Thermal_Conductivity_Const_E, T, cprop.Critical_Temperature)
            ElseIf cprop.IsIon Or cprop.IsSalt Then
                val = 0.0#
            Else
                val = Auxiliary.PROPS.condtg_elyhanley(T, cprop.Critical_Temperature, cprop.Critical_Volume / 1000, cprop.Critical_Compressibility, cprop.Acentric_Factor, cprop.Molar_Weight, Me.AUX_CPi(cprop.Name, T) * cprop.Molar_Weight - 8.314)
            End If

            Return val

        End Function

        Public Overridable Function AUX_VAPVISCm(ByVal T As Double, ByVal RHO As Double, ByVal MM As Double) As Double

            Dim val As Double = 0.0#

            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                val += subst.MoleFraction.GetValueOrDefault * Me.AUX_VAPVISCi(subst.ConstantProperties, T)
            Next

            val = Auxiliary.PROPS.viscg_jossi_stiel_thodos(val, T, MM / RHO / 1000, AUX_TCM(Phase.Vapor), AUX_PCM(Phase.Vapor), AUX_VCM(Phase.Vapor), AUX_MMM(Phase.Vapor))

            Return val

        End Function

        Public Overridable Function AUX_VAPVISCi(ByVal cprop As Interfaces.ICompoundConstantProperties, ByVal T As Double) As Double

            Dim val As Double

            If cprop.VaporViscosityEquation <> "" And cprop.VaporViscosityEquation <> "0" And Not cprop.IsIon And Not cprop.IsSalt Then
                val = Me.CalcCSTDepProp(cprop.VaporViscosityEquation, cprop.Vapor_Viscosity_Const_A, cprop.Vapor_Viscosity_Const_B, cprop.Vapor_Viscosity_Const_C, cprop.Vapor_Viscosity_Const_D, cprop.Vapor_Viscosity_Const_E, T, cprop.Critical_Temperature)
            ElseIf cprop.IsIon Or cprop.IsSalt Then
                val = 0.0#
            Else
                If cprop.Critical_Temperature > 0.0# Then val = Auxiliary.PROPS.viscg_lucas(T, cprop.Critical_Temperature, cprop.Critical_Pressure, cprop.Acentric_Factor, cprop.Molar_Weight) Else val = 0.0#
            End If

            Return val

        End Function

        Public Overridable Function AUX_SOLIDDENS() As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound
            Dim zerodens As Double = 0
            Dim db As String
            Dim T As Double = Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault

            For Each subst In Me.CurrentMaterialStream.Phases(7).Compounds.Values
                db = subst.ConstantProperties.OriginalDB
                If db = "ChemSep" Or (db = "User" And subst.ConstantProperties.SolidDensityEquation <> "") Then
                    Dim A, B, C, D, E, result As Double
                    Dim eqno As String = subst.ConstantProperties.SolidDensityEquation
                    Dim mw As Double = subst.ConstantProperties.Molar_Weight
                    A = subst.ConstantProperties.Solid_Density_Const_A
                    B = subst.ConstantProperties.Solid_Density_Const_B
                    C = subst.ConstantProperties.Solid_Density_Const_C
                    D = subst.ConstantProperties.Solid_Density_Const_D
                    E = subst.ConstantProperties.Solid_Density_Const_E
                    result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'kmol/m3
                    val += subst.MassFraction.GetValueOrDefault * 1 / (result * mw)
                Else
                    If subst.ConstantProperties.SolidDensityAtTs <> 0.0# Then
                        val += subst.MassFraction.GetValueOrDefault * 1 / subst.ConstantProperties.SolidDensityAtTs
                    Else
                        zerodens += subst.MassFraction.GetValueOrDefault
                    End If
                End If
            Next

            Return 1 / val / (1 - zerodens)

        End Function

        Public Overridable Function AUX_SOLIDDENSi(cprop As Interfaces.ICompoundConstantProperties, T As Double) As Double

            Dim val As Double
            Dim zerodens As Double = 0

            If cprop.OriginalDB = "ChemSep" Or (cprop.OriginalDB = "User" And cprop.SolidDensityEquation <> "") Then
                Dim A, B, C, D, E, result As Double
                Dim eqno As String = cprop.SolidDensityEquation
                Dim mw As Double = cprop.Molar_Weight
                A = cprop.Solid_Density_Const_A
                B = cprop.Solid_Density_Const_B
                C = cprop.Solid_Density_Const_C
                D = cprop.Solid_Density_Const_D
                E = cprop.Solid_Density_Const_E
                result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'kmol/m3
                val = 1 / (result * mw)
            Else
                If cprop.SolidDensityAtTs <> 0.0# Then
                    val = 1 / cprop.SolidDensityAtTs
                Else
                    val = 1.0E+20
                End If
            End If

            Return 1 / val

        End Function

        Public Overridable Function AUX_SolidHeatCapacity(ByVal cprop As Interfaces.ICompoundConstantProperties, ByVal T As Double) As Double

            Dim val As Double

            If cprop.OriginalDB = "ChemSep" Or (cprop.OriginalDB = "User" And cprop.SolidDensityEquation <> "") Then
                Dim A, B, C, D, E, result As Double
                Dim eqno As String = cprop.SolidHeatCapacityEquation
                Dim mw As Double = cprop.Molar_Weight
                A = cprop.Solid_Heat_Capacity_Const_A
                B = cprop.Solid_Heat_Capacity_Const_B
                C = cprop.Solid_Heat_Capacity_Const_C
                D = cprop.Solid_Heat_Capacity_Const_D
                E = cprop.Solid_Heat_Capacity_Const_E
                result = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) 'J/kmol/K
                val = result / 1000 / mw 'kJ/kg.K
            Else
                val = 3 ' replacement if no params available
            End If

            Return val

        End Function

        Public Function AUX_SOLIDCP(ByVal Vxm As Array, ByVal cprops As List(Of Interfaces.ICompoundConstantProperties), ByVal T As Double) As Double

            Dim n As Integer = UBound(Vxm)
            Dim val As Double = 0
            For i As Integer = 0 To n
                val += Vxm(i) * AUX_SolidHeatCapacity(cprops(i), T)
            Next
            Return val

        End Function

        Public Overridable Function AUX_LIQDENS(ByVal T As Double, Optional ByVal P As Double = 0, Optional ByVal Pvp As Double = 0, Optional ByVal phaseid As Integer = 3, Optional ByVal FORCE_EOS As Boolean = False) As Double

            Dim val As Double
            Dim m_pr2 As New PropertyPackages.Auxiliary.PengRobinson

            If phaseid = 1 Then

                If T / Me.AUX_TCM(Phase.Liquid) > 1 Then

                    Dim Z = m_pr2.Z_PR(T, P, RET_VMOL(Phase.Liquid), RET_VKij(), RET_VTC, RET_VPC, RET_VW, "L")
                    val = 1 / (8.314 * Z * T / P)
                    val = val * Me.AUX_MMM(Phase.Liquid) / 1000

                Else

                    If Me.Parameters.ContainsKey("PP_IDEAL_MIXRULE_LIQDENS") Then
                        If Convert.ToInt32(Me.Parameters("PP_IDEAL_MIXRULE_LIQDENS")) = 1 Then
                            Dim i As Integer
                            Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
                            i = 0
                            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                                If Me.Parameters.ContainsKey("PP_USEEXPLIQDENS") Then
                                    If Convert.ToInt32(Me.Parameters("PP_USEEXPLIQDENS")) = 1 Then
                                        If subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                                            vk(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                                            vk(i) = subst.ConstantProperties.Molar_Weight * vk(i)
                                        Else
                                            vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                        End If
                                    Else
                                        vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                    End If
                                Else
                                    vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                End If
                                vk(i) = subst.MassFraction / vk(i)
                                i = i + 1
                            Next
                            val = 1 / MathEx.Common.Sum(vk)
                        Else
                            val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid), Me.AUX_PCM(Phase.Liquid), Me.AUX_WM(Phase.Liquid), Me.AUX_MMM(Phase.Liquid), Me.AUX_ZRAM(Phase.Liquid), P, Me.AUX_PVAPM(T))
                        End If
                    Else
                        val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid), Me.AUX_PCM(Phase.Liquid), Me.AUX_WM(Phase.Liquid), Me.AUX_MMM(Phase.Liquid), Me.AUX_ZRAM(Phase.Liquid), P, Me.AUX_PVAPM(T))
                    End If

                End If

            ElseIf phaseid = 2 Then

                If T / Me.AUX_TCM(Phase.Vapor) > 1 Then

                    Dim Z = m_pr2.Z_PR(T, P, RET_VMOL(Phase.Vapor), RET_VKij(), RET_VTC, RET_VPC, RET_VW, "L")
                    val = 1 / (8.314 * Z * T / P)
                    val = val * Me.AUX_MMM(Phase.Vapor) / 1000

                Else

                    Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
                    val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Vapor), Me.AUX_PCM(Phase.Vapor), Me.AUX_WM(Phase.Vapor), Me.AUX_MMM(Phase.Vapor), Me.AUX_ZRAM(Phase.Vapor), P, Me.AUX_PVAPM(T))

                End If

            ElseIf phaseid = 3 Then

                If T / Me.AUX_TCM(Phase.Liquid1) > 1 Then

                    Dim Z = m_pr2.Z_PR(T, P, RET_VMOL(Phase.Liquid1), RET_VKij(), RET_VTC, RET_VPC, RET_VW, "L")
                    val = 1 / (8.314 * Z * T / P)
                    val = val * Me.AUX_MMM(Phase.Liquid1) / 1000

                Else

                    If Me.Parameters.ContainsKey("PP_IDEAL_MIXRULE_LIQDENS") Then
                        If Convert.ToInt32(Me.Parameters("PP_IDEAL_MIXRULE_LIQDENS")) = 1 Then
                            Dim i As Integer
                            Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
                            i = 0
                            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                                If Me.Parameters.ContainsKey("PP_USEEXPLIQDENS") Then
                                    If Convert.ToInt32(Me.Parameters("PP_USEEXPLIQDENS")) = 1 Then
                                        If T < subst.ConstantProperties.Critical_Temperature And subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                                            vk(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                                            vk(i) = subst.ConstantProperties.Molar_Weight * vk(i)
                                        ElseIf T > subst.ConstantProperties.Critical_Temperature Then
                                            vk(i) = 800
                                        ElseIf subst.ConstantProperties.IsIon Or subst.ConstantProperties.IsSalt Then
                                            vk(i) = 1.0E+20
                                        Else
                                            vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                        End If
                                    Else
                                        vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                    End If
                                Else
                                    vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                End If
                                vk(i) = subst.MassFraction / vk(i)
                                i = i + 1
                            Next
                            val = 1 / MathEx.Common.Sum(vk)
                        Else
                            val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid1), Me.AUX_PCM(Phase.Liquid1), Me.AUX_WM(Phase.Liquid1), Me.AUX_MMM(Phase.Liquid1), Me.AUX_ZRAM(Phase.Liquid1), P, Me.AUX_PVAPM(T))
                        End If
                    Else
                        val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid1), Me.AUX_PCM(Phase.Liquid1), Me.AUX_WM(Phase.Liquid1), Me.AUX_MMM(Phase.Liquid1), Me.AUX_ZRAM(Phase.Liquid1), P, Me.AUX_PVAPM(T))
                    End If

                End If

            ElseIf phaseid = 4 Then

                If T / Me.AUX_TCM(Phase.Liquid2) > 1 Then

                    Dim Z = m_pr2.Z_PR(T, P, RET_VMOL(Phase.Liquid2), RET_VKij(), RET_VTC, RET_VPC, RET_VW, "L")
                    val = 1 / (8.314 * Z * T / P)
                    val = val * Me.AUX_MMM(Phase.Liquid2) / 1000

                Else

                    If Me.Parameters.ContainsKey("PP_IDEAL_MIXRULE_LIQDENS") Then
                        If Convert.ToInt32(Me.Parameters("PP_IDEAL_MIXRULE_LIQDENS")) = 1 Then
                            Dim i As Integer
                            Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
                            i = 0
                            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                                If Me.Parameters.ContainsKey("PP_USEEXPLIQDENS") Then
                                    If Convert.ToInt32(Me.Parameters("PP_USEEXPLIQDENS")) = 1 Then
                                        If T < subst.ConstantProperties.Critical_Temperature And subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                                            vk(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                                            vk(i) = subst.ConstantProperties.Molar_Weight * vk(i)
                                        ElseIf T > subst.ConstantProperties.Critical_Temperature Then
                                            vk(i) = 800
                                        Else
                                            vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                        End If
                                    Else
                                        vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                    End If
                                Else
                                    vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                End If
                                vk(i) = subst.MassFraction / vk(i)
                                i = i + 1
                            Next
                            val = 1 / MathEx.Common.Sum(vk)
                        Else
                            val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid2), Me.AUX_PCM(Phase.Liquid2), Me.AUX_WM(Phase.Liquid2), Me.AUX_MMM(Phase.Liquid2), Me.AUX_ZRAM(Phase.Liquid2), P, Me.AUX_PVAPM(T))
                        End If
                    Else
                        val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid2), Me.AUX_PCM(Phase.Liquid2), Me.AUX_WM(Phase.Liquid2), Me.AUX_MMM(Phase.Liquid2), Me.AUX_ZRAM(Phase.Liquid2), P, Me.AUX_PVAPM(T))
                    End If

                End If

            ElseIf phaseid = 5 Then

                If T / Me.AUX_TCM(Phase.Liquid3) > 1 Then

                    Dim Z = m_pr2.Z_PR(T, P, RET_VMOL(Phase.Liquid3), RET_VKij(), RET_VTC, RET_VPC, RET_VW, "L")
                    val = 1 / (8.314 * Z * T / P)
                    val = val * Me.AUX_MMM(Phase.Liquid3) / 1000

                Else

                    If Me.Parameters.ContainsKey("PP_IDEAL_MIXRULE_LIQDENS") Then
                        If Convert.ToInt32(Me.Parameters("PP_IDEAL_MIXRULE_LIQDENS")) = 1 Then
                            Dim i As Integer
                            Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
                            i = 0
                            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                                If Me.Parameters.ContainsKey("PP_USEEXPLIQDENS") Then
                                    If Convert.ToInt32(Me.Parameters("PP_USEEXPLIQDENS")) = 1 Then
                                        If T < subst.ConstantProperties.Critical_Temperature And subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                                            vk(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                                            vk(i) = subst.ConstantProperties.Molar_Weight * vk(i)
                                        ElseIf T > subst.ConstantProperties.Critical_Temperature Then
                                            vk(i) = 800
                                        Else
                                            vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                        End If
                                    Else
                                        vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                    End If
                                Else
                                    vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                End If
                                vk(i) = subst.MassFraction / vk(i)
                                i = i + 1
                            Next
                            val = 1 / MathEx.Common.Sum(vk)
                        Else
                            val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid3), Me.AUX_PCM(Phase.Liquid3), Me.AUX_WM(Phase.Liquid3), Me.AUX_MMM(Phase.Liquid3), Me.AUX_ZRAM(Phase.Liquid3), P, Me.AUX_PVAPM(T))
                        End If
                    Else
                        val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Liquid3), Me.AUX_PCM(Phase.Liquid3), Me.AUX_WM(Phase.Liquid3), Me.AUX_MMM(Phase.Liquid3), Me.AUX_ZRAM(Phase.Liquid3), P, Me.AUX_PVAPM(T))
                    End If

                End If

            ElseIf phaseid = 6 Then

                If T / Me.AUX_TCM(Phase.Aqueous) > 1 Then

                    Dim Z = m_pr2.Z_PR(T, P, RET_VMOL(Phase.Aqueous), RET_VKij(), RET_VTC, RET_VPC, RET_VW, "L")
                    val = 1 / (8.314 * Z * T / P)
                    val = val * Me.AUX_MMM(Phase.Aqueous) / 1000

                Else

                    If Me.Parameters.ContainsKey("PP_IDEAL_MIXRULE_LIQDENS") Then
                        If Convert.ToInt32(Me.Parameters("PP_IDEAL_MIXRULE_LIQDENS")) = 1 Then
                            Dim i As Integer
                            Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
                            i = 0
                            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                                If Me.Parameters.ContainsKey("PP_USEEXPLIQDENS") Then
                                    If Convert.ToInt32(Me.Parameters("PP_USEEXPLIQDENS")) = 1 Then
                                        If subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                                            vk(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                                            vk(i) = subst.ConstantProperties.Molar_Weight * vk(i)
                                        Else
                                            vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                        End If
                                    Else
                                        vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                    End If
                                Else
                                    vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                                End If
                                vk(i) = subst.MassFraction / vk(i)
                                i = i + 1
                            Next
                            val = 1 / MathEx.Common.Sum(vk)
                        Else
                            val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Aqueous), Me.AUX_PCM(Phase.Aqueous), Me.AUX_WM(Phase.Aqueous), Me.AUX_MMM(Phase.Aqueous), Me.AUX_ZRAM(Phase.Aqueous), P, Me.AUX_PVAPM(T))
                        End If
                    Else
                        val = Auxiliary.PROPS.liq_dens_rackett(T, Me.AUX_Rackett_Tcm(Phase.Aqueous), Me.AUX_PCM(Phase.Aqueous), Me.AUX_WM(Phase.Aqueous), Me.AUX_MMM(Phase.Aqueous), Me.AUX_ZRAM(Phase.Aqueous), P, Me.AUX_PVAPM(T))
                    End If

                End If
            End If

            m_pr2 = Nothing

            Return val

        End Function

        Public Overridable Function AUX_LIQDENS(ByVal T As Double, ByVal Vx As Array, Optional ByVal P As Double = 0, Optional ByVal Pvp As Double = 0, Optional ByVal FORCE_EOS As Boolean = False) As Double



            Dim val As Double
            Dim m_pr2 As New PropertyPackages.Auxiliary.PengRobinson

            Dim i As Integer
            Dim vk(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            i = 0
            For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(1).Compounds.Values
                If Me.Parameters.ContainsKey("PP_USEEXPLIQDENS") Then
                    If Convert.ToInt32(Me.Parameters("PP_USEEXPLIQDENS")) = 1 Then
                        If subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                            vk(i) = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                            vk(i) = subst.ConstantProperties.Molar_Weight * vk(i)
                        Else
                            vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                        End If
                    Else
                        vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                    End If
                Else
                    vk(i) = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, P, Me.AUX_PVAPi(subst.Name, T))
                End If
                If T > subst.ConstantProperties.Critical_Temperature Then
                    vk(i) = 1.0E+20
                End If
                If Not Double.IsNaN(vk(i)) Then vk(i) = Vx(i) / vk(i) Else vk(i) = 0.0#
                i = i + 1
            Next
            val = 1 / MathEx.Common.Sum(vk)

            m_pr2 = Nothing

            Return val 'kg/m3

        End Function

        Public Overridable Function AUX_LIQDENSi(ByVal subst As Interfaces.ICompound, ByVal T As Double) As Double



            Dim val As Double

            If subst.ConstantProperties.LiquidDensityEquation <> "" And subst.ConstantProperties.LiquidDensityEquation <> "0" And Not subst.ConstantProperties.IsIon And Not subst.ConstantProperties.IsSalt Then
                val = Me.CalcCSTDepProp(subst.ConstantProperties.LiquidDensityEquation, subst.ConstantProperties.Liquid_Density_Const_A, subst.ConstantProperties.Liquid_Density_Const_B, subst.ConstantProperties.Liquid_Density_Const_C, subst.ConstantProperties.Liquid_Density_Const_D, subst.ConstantProperties.Liquid_Density_Const_E, T, subst.ConstantProperties.Critical_Temperature)
                val = subst.ConstantProperties.Molar_Weight * val
            Else
                val = Auxiliary.PROPS.liq_dens_rackett(T, subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor, subst.ConstantProperties.Molar_Weight, subst.ConstantProperties.Z_Rackett, 101325, Me.AUX_PVAPi(subst.Name, T))
            End If

            Return val 'kg/m3

        End Function

        Public Overridable Function AUX_LIQDENSi(ByVal cprop As Interfaces.ICompoundConstantProperties, ByVal T As Double) As Double

            Dim val As Double

            If cprop.LiquidDensityEquation <> "" And cprop.LiquidDensityEquation <> "0" And Not cprop.IsIon And Not cprop.IsSalt Then
                val = Me.CalcCSTDepProp(cprop.LiquidDensityEquation, cprop.Liquid_Density_Const_A, cprop.Liquid_Density_Const_B, cprop.Liquid_Density_Const_C, cprop.Liquid_Density_Const_D, cprop.Liquid_Density_Const_E, T, cprop.Critical_Temperature)
                val = cprop.Molar_Weight * val
            Else
                val = Auxiliary.PROPS.liq_dens_rackett(T, cprop.Critical_Temperature, cprop.Critical_Pressure, cprop.Acentric_Factor, cprop.Molar_Weight, cprop.Z_Rackett, 101325, Me.AUX_PVAPi(cprop.Name, T))
            End If

            Return val 'kg/m3

        End Function

        Public Function AUX_LIQCPm(ByVal T As Double, ByVal phaseid As Integer) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            val = 0
            For Each subst In Me.CurrentMaterialStream.Phases(phaseid).Compounds.Values
                val += subst.MassFraction.GetValueOrDefault * Me.AUX_LIQ_Cpi(subst.ConstantProperties, T)
            Next

            Return val

        End Function

        Public Overridable Function AUX_LIQ_Cpi(ByVal cprop As Interfaces.ICompoundConstantProperties, ByVal T As Double) As Double

            Dim val As Double
            If T >= cprop.Critical_Temperature Then
                'surrogate for supercritical gases solved in liquid
                val = Me.CalcCSTDepProp(cprop.IdealgasCpEquation, cprop.Ideal_Gas_Heat_Capacity_Const_A, cprop.Ideal_Gas_Heat_Capacity_Const_B, cprop.Ideal_Gas_Heat_Capacity_Const_C, cprop.Ideal_Gas_Heat_Capacity_Const_D, cprop.Ideal_Gas_Heat_Capacity_Const_E, T, cprop.Critical_Temperature)
                val = val / 1000 / cprop.Molar_Weight 'kJ/kg.K
            Else
                If cprop.LiquidHeatCapacityEquation <> "" And cprop.LiquidHeatCapacityEquation <> "0" And Not cprop.IsIon And Not cprop.IsSalt Then
                    val = Me.CalcCSTDepProp(cprop.LiquidHeatCapacityEquation, cprop.Liquid_Heat_Capacity_Const_A, cprop.Liquid_Heat_Capacity_Const_B, cprop.Liquid_Heat_Capacity_Const_C, cprop.Liquid_Heat_Capacity_Const_D, cprop.Liquid_Heat_Capacity_Const_E, T, cprop.Critical_Temperature)
                    val = val / 1000 / cprop.Molar_Weight 'kJ/kg.K
                Else
                    'estimate using Rownlinson/Bondi correlation
                    val = Auxiliary.PROPS.Cpl_rb(AUX_CPi(cprop.Name, T), T, cprop.Critical_Temperature, cprop.Acentric_Factor, cprop.Molar_Weight) 'kJ/kg.K
                End If
            End If

            Return val

        End Function

        Public MustOverride Function AUX_VAPDENS(ByVal T As Double, ByVal P As Double) As Double

        Public Function AUX_INT_CPDTi(ByVal T1 As Double, ByVal T2 As Double, ByVal subst As String) As Double

            Dim deltaT As Double = (T2 - T1) / 10
            Dim i As Integer
            Dim integral, Ti As Double

            Ti = T1 + deltaT / 2

            If Calculator.EnableParallelProcessing Then
                Dim values As New Concurrent.ConcurrentBag(Of Double)
                Parallel.For(0, 10, Sub(ii)
                                        values.Add(Me.AUX_CPi(subst, Ti + deltaT * ii) * deltaT)
                                    End Sub)
                integral = values.Sum
            Else
                integral = 0.0#
                For i = 0 To 9
                    integral += Me.AUX_CPi(subst, Ti) * deltaT
                    Ti += deltaT
                Next
            End If

            Return integral

        End Function

        Public Function AUX_INT_CPDTi_L(ByVal T1 As Double, ByVal T2 As Double, ByVal subst As String) As Double

            Dim deltaT As Double = (T2 - T1) / 10
            Dim Ti, Tc As Double
            Dim i As Integer = 0
            Dim integral As Double = 0

            Ti = T1 + deltaT / 2
            Tc = Me.CurrentMaterialStream.Phases(0).Compounds(subst).ConstantProperties.Critical_Temperature
            For i = 0 To 9
                If Ti > Tc Then
                    integral += Me.AUX_CPi(subst, Ti) * deltaT
                Else
                    integral += Me.AUX_LIQ_Cpi(Me.CurrentMaterialStream.Phases(0).Compounds(subst).ConstantProperties, Ti) * deltaT
                End If

                Ti += deltaT
            Next

            Return integral 'KJ/Kg

        End Function

        Public Function AUX_INT_CPDT_Ti(ByVal T1 As Double, ByVal T2 As Double, ByVal subst As String) As Double

            Dim deltaT As Double = (T2 - T1) / 10
            Dim i As Integer
            Dim integral, Ti As Double

            Ti = T1 + deltaT / 2

            If Calculator.EnableParallelProcessing Then
                Dim values As New Concurrent.ConcurrentBag(Of Double)
                Parallel.For(0, 10, Sub(ii)
                                        values.Add(Me.AUX_CPi(subst, Ti + deltaT * ii) * deltaT / (Ti + deltaT * (ii - 1)))
                                    End Sub)
                integral = values.Sum
            Else
                integral = 0.0#
                For i = 0 To 9
                    integral += Me.AUX_CPi(subst, Ti) * deltaT / (Ti - deltaT)
                    Ti += deltaT
                Next
            End If

            Return integral 'KJ/Kg

        End Function

        Public Function AUX_INT_CPDTm(ByVal T1 As Double, ByVal T2 As Double, ByVal Phase As Phase) As Double

            Dim val As Double = 0
            Dim subst As Interfaces.ICompound

            Select Case Phase

                Case PropertyPackages.Phase.Mixture

                    For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                            subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                        End If
                        val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                    Next

                Case PropertyPackages.Phase.Liquid

                    For Each subst In Me.CurrentMaterialStream.Phases(1).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                            subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                        End If
                        val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                    Next

                Case PropertyPackages.Phase.Liquid1

                    For Each subst In Me.CurrentMaterialStream.Phases(3).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                            subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                        End If
                        val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                    Next

                Case PropertyPackages.Phase.Liquid2

                    For Each subst In Me.CurrentMaterialStream.Phases(4).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                            subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                        End If
                        val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                    Next

                Case PropertyPackages.Phase.Liquid3

                    For Each subst In Me.CurrentMaterialStream.Phases(5).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                            subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                        End If
                        val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                    Next

                Case PropertyPackages.Phase.Vapor

                    For Each subst In Me.CurrentMaterialStream.Phases(2).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                            subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                        End If
                        val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                    Next

            End Select

            Return val

        End Function

        Public Function AUX_INT_CPDT_Tm(ByVal T1 As Double, ByVal T2 As Double, ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                If subst.MoleFraction.GetValueOrDefault <> 0.0# And subst.MassFraction.GetValueOrDefault = 0.0# Then
                    subst.MassFraction = Me.AUX_CONVERT_MOL_TO_MASS(subst.Name, Me.RET_PHASEID(Phase))
                End If
                val += subst.MassFraction.GetValueOrDefault * Me.AUX_INT_CPDT_Ti(T1, T2, subst.Name)
            Next

            Return val

        End Function

        Public Function AUX_DELGF_T(ByVal T1 As Double, ByVal T2 As Double, ByVal id As String, Optional ByVal mode2 As Boolean = False) As Double

            Dim dA As Double = 0
            Dim dB As Double = 0
            Dim dC As Double = 0
            Dim dD As Double = 0
            Dim dE As Double = 0
            Dim dHr As Double = 0
            Dim dGr As Double = 0
            Dim term3 As Double = 0

            Dim int1, int2 As Double
            Dim R = 8.314

            With Me.CurrentMaterialStream.Phases(0).Compounds(id).ConstantProperties

                dHr = .IG_Enthalpy_of_Formation_25C
                dGr = .IG_Gibbs_Energy_of_Formation_25C

                If mode2 Then
                    If .IsIon Or .IsSalt Then
                        term3 = .Electrolyte_Cp0 * 1000 / .Molar_Weight * (Log(T2 / T1) + (T1 / T2) - 1) / R
                    Else
                        int1 = Me.AUX_INT_CPDTi(T1, T2, id)
                        int2 = Me.AUX_INT_CPDT_Ti(T1, T2, id)
                        term3 = int1 / (R * T2) - int2 / R
                    End If
                Else
                    int1 = Me.AUX_INT_CPDTi(T1, T2, id)
                    int2 = Me.AUX_INT_CPDT_Ti(T1, T2, id)
                    term3 = int1 / (R * T2) - int2 / R
                End If


            End With

            Dim result As Double

            If mode2 Then
                result = dGr / (R * T1) + dHr / R * (1 / T2 - 1 / T1) + term3
            Else
                result = (dGr - dHr) / (R * T1) + dHr / (R * T2) + term3
            End If

            Return result

        End Function

        Public Function AUX_DELGig_RT(ByVal T1 As Double, ByVal T2 As Double, ByVal ID() As String, ByVal stcoeff() As Double, ByVal baseID As Integer, Optional ByVal mode2 As Boolean = False) As Double Implements Interfaces.IPropertyPackage.AUX_DELGig_RT

            Dim n As Integer = ID.Length

            Dim int1(n - 1), int2(n - 1), sint1, sint2, dgfT(n - 1), sumdgft As Double

            Dim dHr As Double = 0
            Dim dGr As Double = 0

            sint1 = 0
            sint2 = 0

            With Me.CurrentMaterialStream

                Dim i As Integer = 0
                sumdgft = 0
                Do
                    dHr += stcoeff(i) * .Phases(0).Compounds(ID(i)).ConstantProperties.IG_Enthalpy_of_Formation_25C * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    dGr += stcoeff(i) * .Phases(0).Compounds(ID(i)).ConstantProperties.IG_Gibbs_Energy_of_Formation_25C * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    int1(i) = stcoeff(i) * Me.AUX_INT_CPDTi(T1, T2, ID(i)) / stcoeff(baseID) * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    int2(i) = stcoeff(i) * Me.AUX_INT_CPDT_Ti(T1, T2, ID(i)) / stcoeff(baseID) * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    sint1 += int1(i)
                    sint2 += int2(i)
                    dgfT(i) = stcoeff(i) * Me.AUX_DELGF_T(T1, T2, ID(i), mode2) * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    sumdgft += dgfT(i) '/ stcoeff(baseID)
                    i = i + 1
                Loop Until i = n

            End With
            dHr /= Abs(stcoeff(baseID))
            dGr /= Abs(stcoeff(baseID))

            Dim R = 8.314

            Dim result = (dGr - dHr) / (R * T1) + dHr / (R * T2) + sint1 / (R * T2) - sint2 / R

            Return sumdgft

        End Function

        Public Function AUX_DELHig_RT(ByVal T1 As Double, ByVal T2 As Double, ByVal ID() As String, ByVal stcoeff() As Double, ByVal baseID As Integer) As Double

            Dim n As Integer = ID.Length

            Dim int1(n - 1), sint1 As Double

            Dim dHr As Double = 0

            sint1 = 0

            With Me.CurrentMaterialStream

                Dim i As Integer = 0
                Do
                    dHr += stcoeff(i) * .Phases(0).Compounds(ID(i)).ConstantProperties.IG_Enthalpy_of_Formation_25C * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    int1(i) = stcoeff(i) * Me.AUX_INT_CPDTi(T1, T2, ID(i)) / Abs(stcoeff(baseID)) * .Phases(0).Compounds(ID(i)).ConstantProperties.Molar_Weight
                    sint1 += int1(i)
                    i = i + 1
                Loop Until i = n

            End With
            dHr /= Abs(stcoeff(baseID))

            Dim result = dHr + sint1

            Return result / (8.314 * T2)

        End Function

        Public Overridable Function AUX_CONVERT_MOL_TO_MASS(ByVal subst As String, ByVal phasenumber As Integer) As Double

            Dim mol_x_mm As Double
            Dim sub1 As Interfaces.ICompound
            For Each sub1 In Me.CurrentMaterialStream.Phases(phasenumber).Compounds.Values
                mol_x_mm += sub1.MoleFraction.GetValueOrDefault * sub1.ConstantProperties.Molar_Weight
            Next

            sub1 = Me.CurrentMaterialStream.Phases(phasenumber).Compounds(subst)
            If mol_x_mm <> 0.0# Then
                Return sub1.MoleFraction.GetValueOrDefault * sub1.ConstantProperties.Molar_Weight / mol_x_mm
            Else
                Return 0.0#
            End If

        End Function

        Public Overridable Function AUX_CONVERT_MASS_TO_MOL(ByVal subst As String, ByVal phasenumber As Integer) As Double

            Dim mass_div_mm As Double
            Dim sub1 As Interfaces.ICompound
            For Each sub1 In Me.CurrentMaterialStream.Phases(phasenumber).Compounds.Values
                mass_div_mm += sub1.MassFraction.GetValueOrDefault / sub1.ConstantProperties.Molar_Weight
            Next

            sub1 = Me.CurrentMaterialStream.Phases(phasenumber).Compounds(subst)
            Return sub1.MassFraction.GetValueOrDefault / sub1.ConstantProperties.Molar_Weight / mass_div_mm

        End Function

        Public Overridable Function DW_CalcSolidEnthalpy(ByVal T As Double, ByVal Vx As Double(), cprops As List(Of Interfaces.ICompoundConstantProperties)) As Double

            Dim n As Integer = UBound(Vx)
            Dim i As Integer
            Dim HS As Double = 0.0#
            Dim Cpi As Double
            Dim VMF() As Double

            VMF = RET_VMM().MultiplyY(Vx).NormalizeY 'calculate mass fractions

            For i = 0 To n
                If Vx(i) > 0 Then
                    If cprops(i).OriginalDB = "ChemSep" Or cprops(i).OriginalDB = "User" Then
                        Dim A, B, C, D, E As Double
                        Dim eqno As String = cprops(i).SolidHeatCapacityEquation
                        Dim mw As Double = cprops(i).Molar_Weight
                        A = cprops(i).Solid_Heat_Capacity_Const_A
                        B = cprops(i).Solid_Heat_Capacity_Const_B
                        C = cprops(i).Solid_Heat_Capacity_Const_C
                        D = cprops(i).Solid_Heat_Capacity_Const_D
                        E = cprops(i).Solid_Heat_Capacity_Const_E
                        '<SolidHeatCapacityCp name="Solid heat capacity"  units="J/kmol/K" >
                        Cpi = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) / 1000 / mw 'kJ/kg.K

                        If cprops(i).TemperatureOfFusion < 298.15 Then
                            HS += VMF(i) * Me.AUX_INT_CPDTi_L(298.15, cprops(i).TemperatureOfFusion, cprops(i).Name)
                            HS -= VMF(i) * cprops(i).EnthalpyOfFusionAtTf * 1000 / mw
                            HS -= VMF(i) * Cpi * (cprops(i).TemperatureOfFusion - T)
                        Else
                            HS -= VMF(i) * cprops(i).EnthalpyOfFusionAtTf * 1000 / mw
                            HS -= VMF(i) * Cpi * (298.15 - T)
                        End If

                    ElseIf cprops(i).TemperatureOfFusion <> 0.0# Then
                        HS += -VMF(i) * cprops(i).EnthalpyOfFusionAtTf * 1000 / cprops(i).Molar_Weight
                    End If
                End If
            Next

            Return HS 'kJ/kg

        End Function

        Public Overridable Function DW_CalcSolidHeatCapacityCp(ByVal T As Double, ByVal Vx As Double(), cprops As List(Of Interfaces.ICompoundConstantProperties)) As Double

            Dim n As Integer = UBound(Vx)
            Dim i As Integer
            Dim Cp As Double = 0.0#
            Dim Cpi As Double
            Dim VMF() As Double

            VMF = RET_VMM().MultiplyY(Vx).NormalizeY 'calculate mass fractions

            For i = 0 To n
                If Vx(i) > 0 Then
                    If cprops(i).OriginalDB = "ChemSep" Or cprops(i).OriginalDB = "User" Then
                        Dim A, B, C, D, E As Double
                        Dim eqno As String = cprops(i).SolidHeatCapacityEquation
                        Dim mw As Double = cprops(i).Molar_Weight
                        A = cprops(i).Solid_Heat_Capacity_Const_A
                        B = cprops(i).Solid_Heat_Capacity_Const_B
                        C = cprops(i).Solid_Heat_Capacity_Const_C
                        D = cprops(i).Solid_Heat_Capacity_Const_D
                        E = cprops(i).Solid_Heat_Capacity_Const_E
                        '<SolidHeatCapacityCp name="Solid heat capacity"  units="J/kmol/K" >
                        Cpi = Me.CalcCSTDepProp(eqno, A, B, C, D, E, T, 0) / 1000 / mw 'kJ/kg.K
                        Cp += VMF(i) * Cpi
                    End If
                End If
            Next

            Return Cp 'kJ/kg.K

        End Function

        Public Function RET_VMOL(ByVal Phase As Phase) As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val(i) = subst.MoleFraction.GetValueOrDefault
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VMM() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Molar_Weight
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VMAS(ByVal Phase As Phase) As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0
            Dim sum As Double = 0.0#

            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                val(i) = subst.MassFraction.GetValueOrDefault
                sum += val(i)
                i += 1
            Next

            If sum <> 0.0# Then
                Return val
            Else
                Return AUX_CONVERT_MOL_TO_MASS(RET_VMOL(Phase))
            End If

        End Function

        Public Function RET_VTC()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Critical_Temperature
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VTF() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.TemperatureOfFusion
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VHF() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.EnthalpyOfFusionAtTf
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VTB() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Normal_Boiling_Point
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VPC() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Critical_Pressure
                i += 1
            Next

            Return val
        End Function

        Public Function RET_VZC() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Critical_Compressibility
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VZRa() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Z_Rackett
                i += 1
            Next
            Return val

        End Function

        Public Function RET_VVC() As Double()

            Dim vc, val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                vc = subst.ConstantProperties.Critical_Volume
                If subst.ConstantProperties.Critical_Volume = 0.0# Then
                    vc = Auxiliary.PROPS.Vc(subst.ConstantProperties.Critical_Temperature, subst.ConstantProperties.Critical_Pressure, subst.ConstantProperties.Acentric_Factor)
                End If
                val(i) = vc
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VW() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Acentric_Factor
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VCP(ByVal T As Double) As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = Me.AUX_CPi(subst.Name, T)
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VHVAP(ByVal T As Double) As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = Me.AUX_HVAPi(subst.Name, T)
                i += 1
            Next

            Return val

        End Function

        Public Function RET_Hid(ByVal T1 As Double, ByVal T2 As Double, ByVal Phase As Phase) As Double

            Dim val As Double
            'Dim subst As Interfaces.ICompound

            Dim phaseID As Integer

            If Phase = PropertyPackages.Phase.Liquid Then phaseID = 1
            If Phase = PropertyPackages.Phase.Liquid1 Then phaseID = 3
            If Phase = PropertyPackages.Phase.Liquid2 Then phaseID = 4
            If Phase = PropertyPackages.Phase.Liquid3 Then phaseID = 5
            If Phase = PropertyPackages.Phase.Vapor Then phaseID = 2
            If Phase = PropertyPackages.Phase.Mixture Then phaseID = 0

            'For Each subst In Me.CurrentMaterialStream.Phases(phaseID).Compounds.Values
            '    val += subst.MassFraction.GetValueOrDefault * subst.ConstantProperties.Enthalpy_of_Formation_25C
            'Next

            Return Me.AUX_INT_CPDTm(T1, T2, Phase) + val

        End Function

        Public Function RET_Hid(ByVal T1 As Double, ByVal T2 As Double, ByVal Vz As Double()) As Double

            Return Me.AUX_INT_CPDTm(T1, T2, Me.AUX_CONVERT_MOL_TO_MASS(Vz))

        End Function

        Public Function RET_Hid_L(ByVal T1 As Double, ByVal T2 As Double, ByVal Vz As Double()) As Double

            Return Me.AUX_INT_CPDTm_L(T1, T2, Me.AUX_CONVERT_MOL_TO_MASS(Vz))

        End Function

        Public Function RET_Sid_L(ByVal T1 As Double, ByVal T2 As Double, ByVal Vz As Double()) As Double

            Return Me.RET_Hid_L(T1, T2, Vz) / T2

        End Function

        Public Function RET_Hid_i(ByVal T1 As Double, ByVal T2 As Double, ByVal id As String) As Double

            Return Me.AUX_INT_CPDTi(T1, T2, id)

        End Function

        Public Function RET_Hid_i_L(ByVal T1 As Double, ByVal T2 As Double, ByVal id As String) As Double

            Return Me.AUX_INT_CPDTi_L(T1, T2, id)

        End Function

        Public Function RET_Sid(ByVal T1 As Double, ByVal T2 As Double, ByVal P2 As Double, ByVal Phase As Phase) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            Dim phaseID As Integer

            If Phase = PropertyPackages.Phase.Liquid Then phaseID = 1
            If Phase = PropertyPackages.Phase.Liquid1 Then phaseID = 3
            If Phase = PropertyPackages.Phase.Liquid2 Then phaseID = 4
            If Phase = PropertyPackages.Phase.Liquid3 Then phaseID = 5
            If Phase = PropertyPackages.Phase.Vapor Then phaseID = 2
            If Phase = PropertyPackages.Phase.Mixture Then phaseID = 0
            'subst.MassFraction.GetValueOrDefault * subst.ConstantProperties.Standard_Absolute_Entropy 
            For Each subst In Me.CurrentMaterialStream.Phases(phaseID).Compounds.Values
                If subst.MoleFraction.GetValueOrDefault <> 0 Then val += -8.314 * subst.MoleFraction.GetValueOrDefault * Math.Log(subst.MoleFraction.GetValueOrDefault) / subst.ConstantProperties.Molar_Weight
            Next

            Dim tmp = Me.AUX_INT_CPDT_Tm(T1, T2, Phase) + val - 8.314 * Math.Log(P2 / 101325) / Me.AUX_MMM(Phase)

            Return tmp

        End Function

        Public Function RET_Sid(ByVal T1 As Double, ByVal T2 As Double, ByVal P2 As Double, ByVal Vz As Double()) As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound
            Dim Vw = Me.AUX_CONVERT_MOL_TO_MASS(Vz)

            Dim i As Integer = 0

            'Vw(i) * subst.ConstantProperties.Standard_Absolute_Entropy
            val = 0
            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                If Vz(i) <> 0 Then val += -8.314 * Vz(i) * Math.Log(Vz(i)) / subst.ConstantProperties.Molar_Weight
                i = i + 1
            Next
            Dim tmp1 = 8.314 * Math.Log(P2 / 101325) / Me.AUX_MMM(Vz)
            Dim tmp2 = Me.AUX_INT_CPDT_Tm(T1, T2, Me.AUX_CONVERT_MOL_TO_MASS(Vz))
            Return tmp2 + val - tmp1

        End Function

        Public Function RET_Sid_i(ByVal T1 As Double, ByVal T2 As Double, ByVal P2 As Double, ByVal id As String) As Double

            Dim val As Double

            Dim tmp1 = 8.314 * Math.Log(P2 / 101325) / Me.CurrentMaterialStream.Phases(0).Compounds(id).ConstantProperties.Molar_Weight
            Dim tmp2 = Me.AUX_INT_CPDT_Ti(T1, T2, id)
            Return tmp2 + val - tmp1

        End Function

        Public Function RET_Gid(ByVal T1 As Double, ByVal T2 As Double, ByVal P2 As Double, ByVal Vz As Double()) As Double

            Dim hid = Me.RET_Hid(T1, T2, Vz)
            Dim sid = Me.RET_Sid(T1, T2, P2, Vz)

            Return hid - T2 * sid

        End Function

        Public Function RET_Gid_i(ByVal T1 As Double, ByVal T2 As Double, ByVal P2 As Double, ByVal id As String) As Double

            Dim hid = Me.RET_Hid_i(T1, T2, id)
            Dim sid = Me.RET_Sid_i(T1, T2, P2, id)

            Return hid - T2 * sid

        End Function

        Public Function RET_Gid(ByVal T1 As Double, ByVal T2 As Double, ByVal P2 As Double, ByVal Phase As Phase) As Double

            Dim hid = Me.RET_Hid(T1, T2, Phase)
            Dim sid = Me.RET_Sid(T1, T2, P2, Phase)

            Return hid - T2 * sid

        End Function

        Public Overridable Function RET_VKij() As Double(,)

            If Me.ParametrosDeInteracao Is Nothing Then
                Me.ParametrosDeInteracao = New DataTable
                With Me.ParametrosDeInteracao.Columns
                    For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                        .Add(subst.ConstantProperties.ID, GetType(System.Double))
                    Next
                End With

                With Me.ParametrosDeInteracao.Rows
                    For Each subst As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                        .Add()
                    Next
                End With
            End If

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1, Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim i As Integer = 0
            Dim l As Integer = 0
            For Each r As DataRow In Me.ParametrosDeInteracao.Rows
                i = 0
                Do
                    If l <> i And r.Item(i).GetType().ToString <> "System.DBNull" Then
                        Dim value As Double
                        If Double.TryParse(r.Item(i), value) Then
                            val(l, i) = r.Item(i)
                        Else
                            val(l, i) = 0
                        End If
                    Else
                        val(l, i) = 0
                    End If
                    i = i + 1
                Loop Until i = Me.ParametrosDeInteracao.Rows.Count
                l = l + 1
            Next

            Return val

        End Function

        Public Function RET_VCSACIDS()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As String
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.COSMODBName
                If val(i) = "" Then val(i) = subst.ConstantProperties.CAS_Number
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VIDS()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As String
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.ID
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VCAS() As String()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As String
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.CAS_Number
                i += 1
            Next

            Return val

        End Function

        Public Function RET_VNAMES() As String()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As String
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = subst.ConstantProperties.Name
                i += 1
            Next

            Return val

        End Function

        Public Function RET_NullVector() As Double()

            Dim val(Me.CurrentMaterialStream.Phases(0).Compounds.Count - 1) As Double
            Dim subst As Interfaces.ICompound
            Dim i As Integer = 0
            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val(i) = 0.0#
                i += 1
            Next
            Return val

        End Function

        Public Function RET_PHASECODE(ByVal phaseID As Integer) As PropertyPackages.Phase
            Select Case phaseID
                Case 0
                    Return Phase.Mixture
                Case 1
                    Return Phase.Liquid
                Case 2
                    Return Phase.Vapor
                Case 3
                    Return Phase.Liquid1
                Case 4
                    Return Phase.Liquid2
                Case 5
                    Return Phase.Liquid3
                Case 6
                    Return Phase.Aqueous
                Case 7
                    Return Phase.Solid
            End Select
        End Function

        Public Function RET_PHASEID(ByVal phasecode As PropertyPackages.Phase) As Integer
            Select Case phasecode
                Case Phase.Mixture
                    Return 0
                Case Phase.Liquid
                    Return 1
                Case Phase.Vapor
                    Return 2
                Case Phase.Liquid1
                    Return 3
                Case Phase.Liquid2
                    Return 4
                Case Phase.Liquid3
                    Return 5
                Case Phase.Aqueous
                    Return 6
                Case Phase.Solid
                    Return 7
                Case Else
                    Return -1
            End Select
        End Function

        Public Overridable Function AUX_MMM(ByVal Vz() As Double, Optional ByVal state As String = "") As Double

            Dim val As Double
            Dim subst As Interfaces.ICompound

            Dim i As Integer = 0

            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val += Vz(i) * subst.ConstantProperties.Molar_Weight
                i += 1
            Next

            Return val

        End Function

        Public Function AUX_NORMALIZE(ByVal Vx() As Double) As Double()

            Dim sum As Double = 0
            Dim i, n As Integer

            n = UBound(Vx)

            Dim Vxnew(n) As Double

            For i = 0 To n
                sum += Vx(i)
            Next

            For i = 0 To n
                Vxnew(i) = Vx(i) / sum
                If Double.IsNaN(Vxnew(i)) Then Vxnew(i) = 0.0#
            Next

            Return Vxnew

        End Function

        Public Function AUX_ERASE(ByVal Vx As Double()) As Double()

            Dim i, n As Integer

            n = UBound(Vx)

            Dim Vx2(n) As Double

            For i = 0 To n
                Vx2(i) = 0
            Next

            Return Vx2

        End Function

        Public Function AUX_SINGLECOMPIDX(ByVal Vx As Object) As Integer

            Dim i, n As Integer

            n = UBound(Vx)

            For i = 0 To n
                If Vx(i) <> 0 Then Return i
            Next

            Return -1

        End Function

        Public Function AUX_SINGLECOMPIDX(ByVal Phase As Phase) As Integer

            Dim i As Integer

            Dim subst As Interfaces.ICompound

            i = 0
            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                If subst.MoleFraction.GetValueOrDefault <> 0 Then Return i
            Next

            Return -1

        End Function

        Public Function AUX_IS_SINGLECOMP(ByVal Vx As Object) As Boolean

            Dim i, c, n As Integer, bo As Boolean

            n = UBound(Vx)

            bo = False
            c = 0
            For i = 0 To n
                If Vx(i) <> 0.0# Then c += 1
                If Me.DW_GetConstantProperties(i).IsBlackOil Then bo = True
            Next

            If c = 1 And Not bo Then Return True Else Return False

        End Function

        Public Function AUX_IS_SINGLECOMP(ByVal Phase As Phase) As Boolean

            Dim c As Integer, bo As Boolean

            Dim subst As Interfaces.ICompound

            bo = False
            c = 0
            For Each subst In Me.CurrentMaterialStream.Phases(Me.RET_PHASEID(Phase)).Compounds.Values
                If subst.MoleFraction.GetValueOrDefault <> 0 Then c += 1
                If subst.ConstantProperties.IsBlackOil Then bo = True
            Next

            If c = 1 And Not bo Then Return True Else Return False

        End Function

        Public Function AUX_INT_CPDTm(ByVal T1 As Double, ByVal T2 As Double, ByVal Vw As Double()) As Double

            Dim val As Double
            Dim i As Integer = 0
            Dim subst As Interfaces.ICompound

            val = 0.0#
            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val += Vw(i) * Me.AUX_INT_CPDTi(T1, T2, subst.Name)
                i += 1
            Next

            Return val

        End Function

        Public Function AUX_INT_CPDTm_L(ByVal T1 As Double, ByVal T2 As Double, ByVal Vw As Double()) As Double

            Dim val As Double
            Dim i As Integer = 0
            Dim subst As Interfaces.ICompound
            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val += Vw(i) * Me.AUX_INT_CPDTi_L(T1, T2, subst.Name)
                i += 1
            Next

            Return val

        End Function

        Public Function AUX_INT_CPDT_Tm(ByVal T1 As Double, ByVal T2 As Double, ByVal Vw As Double()) As Double

            Dim val As Double
            Dim i As Integer = 0
            Dim subst As Interfaces.ICompound
            For Each subst In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                val += Vw(i) * Me.AUX_INT_CPDT_Ti(T1, T2, subst.Name)
                i += 1
            Next

            Return val

        End Function

        Public Overridable Function AUX_CONVERT_MOL_TO_MASS(ByVal Vz As Double()) As Double()

            Dim Vwe(UBound(Vz)) As Double
            Dim mol_x_mm As Double = 0
            Dim i As Integer = 0
            Dim sub1 As Interfaces.ICompound
            For Each sub1 In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                mol_x_mm += Vz(i) * sub1.ConstantProperties.Molar_Weight
                i += 1
            Next

            i = 0
            For Each sub1 In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                If mol_x_mm <> 0 Then
                    Vwe(i) = Vz(i) * sub1.ConstantProperties.Molar_Weight / mol_x_mm
                Else
                    Vwe(i) = 0.0#
                End If
                i += 1
            Next

            Return Vwe

        End Function

        Public Overridable Function AUX_CONVERT_MASS_TO_MOL(ByVal Vz As Double()) As Double()

            Dim Vw(UBound(Vz)) As Double
            Dim mass_div_mm As Double
            Dim i As Integer = 0
            Dim sub1 As Interfaces.ICompound
            For Each sub1 In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                mass_div_mm += Vz(i) / sub1.ConstantProperties.Molar_Weight
                i += 1
            Next

            i = 0
            For Each sub1 In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                Vw(i) = Vz(i) / sub1.ConstantProperties.Molar_Weight / mass_div_mm
                i += 1
            Next

            Return Vw

        End Function

        Public Function AUX_CheckTrivial(ByVal KI As Double()) As Boolean

            Dim isTrivial As Boolean = True
            Dim n, i As Integer
            n = UBound(KI)

            For i = 0 To n
                If Abs(KI(i) - 1) > 0.01 Then isTrivial = False
            Next

            Return isTrivial

        End Function

        Function CalcCSTDepProp(ByVal eqno As String, ByVal A As Double, ByVal B As Double, ByVal C As Double, ByVal D As Double, ByVal E As Double, ByVal T As Double, ByVal Tc As Double) As Double

            Dim Tr As Double = T / Tc

            Select Case eqno
                Case "1"
                    Return A
                Case "2"
                    Return A + B * T
                Case "3"
                    Return A + B * T + C * T ^ 2
                Case "4"
                    Return A + B * T + C * T ^ 2 + D * T ^ 3
                Case "5"
                    Return A + B * T + C * T ^ 2 + D * T ^ 3 + E * T ^ 4
                Case "10"
                    Return Exp(A - B / (T + C))
                Case "11"
                    Return Exp(A)
                Case "12"
                    Return Exp(A + B * T)
                Case "13"
                    Return Exp(A + B * T + C * T ^ 2)
                Case "14"
                    Return Exp(A + B * T + C * T ^ 2 + D * T ^ 3)
                Case "15"
                    Return Exp(A + B * T + C * T ^ 2 + D * T ^ 3 + E * T ^ 4)
                Case "16"
                    Return A + Exp(B / T + C + D * T + E * T ^ 2)
                Case "17"
                    Return A + Exp(B + C * T + D * T ^ 2 + E * T ^ 3)
                Case "45"
                    Return A * T + B * T ^ 2 / 2 + C * T ^ 3 / 3 + D * T ^ 4 / 4 + E * T ^ 5 / 5
                Case "75"
                    Return B + 2 * C * T + 3 * D * T ^ 2 + 4 * E * T ^ 3
                Case "100"
                    Return A + B * T + C * T ^ 2 + D * T ^ 3 + E * T ^ 4
                Case "101"
                    Return Exp(A + B / T + C * Log(T) + D * T ^ E)
                Case "102"
                    Return A * T ^ B / (1 + C / T + D / T ^ 2)
                Case "103"
                    Return A + B * Exp(-C / (T ^ D))
                Case "104"
                    Return A + B / T + C / T ^ 2 + D / T ^ 8 + E / T ^ 9
                Case "105"
                    Return A / (B ^ (1 + (1 - T / C) ^ D))
                Case "106"
                    Return A * (1 - Tr) ^ (B + C * Tr + D * Tr ^ 2 + E * Tr ^ 3)
                Case "107"
                    Return A + B * (C / T / Sinh(C / T)) ^ 2 + D * (D / T / Cosh(D / T)) ^ 2
                Case "114"
                    Return A * T + B * T ^ 2 / 2 + C * T ^ 3 / 3 + D * T ^ 4 / 4
                Case "115"
                    Return Exp(A + B / T + C * Log(T) + D * T ^ 2 + E / T ^ 2)
                Case "116"
                    Return A + B * (1 - Tr) ^ 0.35 + C * (1 - Tr) ^ (2 / 3) + D * (1 - Tr) + E * (1 - Tr) ^ (4 / 3)
                Case "117"
                    Return A * T + B * (C / T) / Tanh(C / T) - D * (E / T) / Tanh(E / T)
                Case "207"
                    Return Exp(A - B / (T + C))
                Case "208"
                    Return 10 ^ (A - B / (T + C))
                Case "209"
                    Return 10 ^ (A * (1 / T - 1 / B))
                Case "210"
                    Return 10 ^ (A + B / T + C * T + D * T ^ 2)
                Case "211"
                    Return A * ((B - T) / (B - C)) ^ D
                Case Else
                    Return 0
            End Select


        End Function

#End Region

#Region "   CAPE-OPEN 1.0 Methods and Properties"

        Private _compdesc, _compname As String

        ''' <summary>
        ''' Gets the name of the component.
        ''' </summary>
        ''' <value></value>
        ''' <returns>CapeString</returns>
        ''' <remarks>Implements CapeOpen.ICapeIdentification.ComponentDescription</remarks>
        Public Overridable Property ComponentDescription() As String Implements CapeOpen.ICapeIdentification.ComponentDescription
            Get
                Return _compdesc
            End Get
            Set(ByVal value As String)
                _compdesc = value
            End Set
        End Property

        ''' <summary>
        ''' Gets the description of the component.
        ''' </summary>
        ''' <value></value>
        ''' <returns>CapeString</returns>
        ''' <remarks>Implements CapeOpen.ICapeIdentification.ComponentName</remarks>
        Public Overridable Property ComponentName() As String Implements CapeOpen.ICapeIdentification.ComponentName
            Get
                Return _compname
            End Get
            Set(ByVal value As String)
                _compname = value
            End Set
        End Property

        ''' <summary>
        ''' Method responsible for calculating/delegating phase equilibria.
        ''' </summary>
        ''' <param name="materialObject">The Material Object</param>
        ''' <param name="flashType">Flash calculation type.</param>
        ''' <param name="props">Properties to be calculated at equilibrium. UNDEFINED for no
        ''' properties. If a list, then the property values should be set for each
        ''' phase present at equilibrium. (not including the overall phase).</param>
        ''' <remarks><para>On the Material Object the CalcEquilibrium method must set the amounts (phaseFraction) and compositions
        ''' (fraction) for all phases present at equilibrium, as well as the temperature and pressure for the overall
        ''' mixture, if these are not set as part of the calculation specifications. The CalcEquilibrium method must not
        ''' set on the Material Object any other value - in particular it must not set any values for phases that do not
        ''' exist. See 5.2.1 for more information.</para>
        ''' <para>The available list of flashes is given in section 5.6.1.</para>
        ''' <para>When calling this method, it is advised not to combine a flash calculation with a property calculation.
        ''' Through the returned error one cannot see which has failed, plus the additional arguments available in a
        ''' CalcProp call (such as calculation type) cannot be specified in a CalcEquilibrium call. Advice is to perform a
        ''' CalcEquilibrium, get the phaseIDs and perform a CalcProp for the existing phases.</para></remarks>
        Public Overridable Sub CalcEquilibrium(ByVal materialObject As Object, ByVal flashType As String, ByVal props As Object) Implements CapeOpen.ICapeThermoPropertyPackage.CalcEquilibrium

            Dim mymat As ICapeThermoMaterial = materialObject
            Me.CurrentMaterialStream = mymat
            Select Case flashType.ToLower
                Case "tp"
                    Me.DW_CalcEquilibrium(FlashSpec.T, FlashSpec.P)
                Case "ph"
                    Me.DW_CalcEquilibrium(FlashSpec.P, FlashSpec.H)
                Case "ps"
                    Me.DW_CalcEquilibrium(FlashSpec.P, FlashSpec.S)
                Case "tvf"
                    Me.DW_CalcEquilibrium(FlashSpec.T, FlashSpec.VAP)
                Case "pvf"
                    Me.DW_CalcEquilibrium(FlashSpec.P, FlashSpec.VAP)
                Case "pt"
                    Me.DW_CalcEquilibrium(FlashSpec.T, FlashSpec.P)
                Case "hp"
                    Me.DW_CalcEquilibrium(FlashSpec.P, FlashSpec.H)
                Case "sp"
                    Me.DW_CalcEquilibrium(FlashSpec.P, FlashSpec.S)
                Case "vft"
                    Me.DW_CalcEquilibrium(FlashSpec.T, FlashSpec.VAP)
                Case "vfp"
                    Me.DW_CalcEquilibrium(FlashSpec.P, FlashSpec.VAP)
                Case Else
                    Throw New CapeOpen.CapeNoImplException()
            End Select

        End Sub

        ''' <summary>
        ''' This method is responsible for doing all calculations on behalf of the Calculation Routine component.
        ''' </summary>
        ''' <param name="materialObject">The Material Object of the calculation.</param>
        ''' <param name="props">The list of properties to be calculated.</param>
        ''' <param name="phases">List of phases for which the properties are to be calculated.</param>
        ''' <param name="calcType">Type of calculation: Mixture Property or Pure Compound Property. For
        ''' partial property, such as fugacity coefficients of compounds in a
        ''' mixture, use “Mixture” CalcType. For pure compound fugacity
        ''' coefficients, use “Pure” CalcType.</param>
        ''' <remarks></remarks>
        Public Overridable Sub CalcProp(ByVal materialObject As Object, ByVal props As Object, ByVal phases As Object, ByVal calcType As String) Implements CapeOpen.ICapeThermoPropertyPackage.CalcProp
            Dim mymat As Interfaces.IMaterialStream = materialObject
            Me.CurrentMaterialStream = mymat
            Dim ph As String() = phases
            For Each f As String In ph
                For Each pi As PhaseInfo In Me.PhaseMappings.Values
                    If f = pi.PhaseLabel Then
                        If Not pi.DWPhaseID = Phase.Solid Then
                            For Each p As String In props
                                Me.DW_CalcProp(p, pi.DWPhaseID)
                            Next
                            Exit For
                        Else
                            Me.DW_CalcSolidPhaseProps()
                        End If
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' Returns the values of the Constant properties of the compounds contained in the passed Material Object.
        ''' </summary>
        ''' <param name="materialObject">The Material Object.</param>
        ''' <param name="props">The list of properties.</param>
        ''' <returns>Compound Constant values.</returns>
        ''' <remarks></remarks>
        Public Overridable Function GetComponentConstant(ByVal materialObject As Object, ByVal props As Object) As Object Implements CapeOpen.ICapeThermoPropertyPackage.GetComponentConstant
            Dim vals As New ArrayList
            Dim mymat As Interfaces.IMaterialStream = materialObject
            For Each c As Interfaces.ICompound In mymat.Phases(0).Compounds.Values
                For Each p As String In props
                    Select Case p.ToLower
                        Case "molecularweight"
                            vals.Add(c.ConstantProperties.Molar_Weight)
                        Case "criticaltemperature"
                            vals.Add(c.ConstantProperties.Critical_Temperature)
                        Case "criticalpressure"
                            vals.Add(c.ConstantProperties.Critical_Pressure)
                        Case "criticalvolume"
                            vals.Add(c.ConstantProperties.Critical_Volume / 1000)
                        Case "criticalcompressibilityfactor"
                            vals.Add(c.ConstantProperties.Critical_Compressibility)
                        Case "acentricfactor"
                            vals.Add(c.ConstantProperties.Acentric_Factor)
                        Case "normalboilingpoint"
                            vals.Add(c.ConstantProperties.Normal_Boiling_Point)
                        Case "idealgasgibbsfreeenergyofformationat25c"
                            vals.Add(c.ConstantProperties.IG_Gibbs_Energy_of_Formation_25C * c.ConstantProperties.Molar_Weight)
                        Case "idealgasenthalpyofformationat25c"
                            vals.Add(c.ConstantProperties.IG_Enthalpy_of_Formation_25C * c.ConstantProperties.Molar_Weight)
                        Case "casregistrynumber"
                            vals.Add(c.ConstantProperties.CAS_Number)
                        Case "chemicalformula"
                            vals.Add(c.ConstantProperties.Formula)
                        Case Else
                            Throw New CapeOpen.CapeNoImplException
                    End Select
                Next
            Next
            Dim arr2(vals.Count) As Double
            Array.Copy(vals.ToArray, arr2, vals.Count)
            Return arr2
        End Function

        ''' <summary>
        ''' Returns the list of compounds of a given Property Package.
        ''' </summary>
        ''' <param name="compIds">List of compound IDs</param>
        ''' <param name="formulae">List of compound formulae</param>
        ''' <param name="names">List of compound names.</param>
        ''' <param name="boilTemps">List of boiling point temperatures.</param>
        ''' <param name="molWt">List of molecular weights.</param>
        ''' <param name="casNo">List of CAS numbers .</param>
        ''' <remarks>Compound identification could be necessary if the PME has internal knowledge of chemical compounds, or in case of
        ''' use of multiple Property Packages. In order to identify the compounds of a Property Package, the PME will use the
        ''' 'casno’ argument instead of the compIds. The reason is that different PMEs may give different names to the same
        ''' chemical compounds, whereas CAS Numbers are universal. Therefore, it is recommended to provide a value for the
        ''' casno argument wherever available.</remarks>
        Public Overridable Sub GetComponentList(ByRef compIds As Object, ByRef formulae As Object, ByRef names As Object, ByRef boilTemps As Object, ByRef molWt As Object, ByRef casNo As Object) Implements CapeOpen.ICapeThermoPropertyPackage.GetComponentList

            Dim ids, formulas, nms, bts, casnos, molws As New ArrayList

            If Calculator.CAPEOPENMode Then
                For Each c As Interfaces.ICompoundConstantProperties In _selectedcomps.Values
                    ids.Add(c.Name)
                    formulas.Add(c.Formula)
                    nms.Add(c.Name)
                    bts.Add(c.Normal_Boiling_Point)
                    If c.CAS_Number <> "" Then casnos.Add(c.CAS_Number) Else casnos.Add(c.Name)
                    molws.Add(c.Molar_Weight)
                Next
            Else
                For Each c As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                    ids.Add(c.ConstantProperties.Name)
                    formulas.Add(c.ConstantProperties.Formula)
                    nms.Add(c.ConstantProperties.Name)
                    bts.Add(c.ConstantProperties.Normal_Boiling_Point)
                    If c.ConstantProperties.CAS_Number <> "" Then casnos.Add(c.ConstantProperties.CAS_Number) Else casnos.Add(c.ConstantProperties.Name)
                    molws.Add(c.ConstantProperties.Molar_Weight)
                Next
            End If

            Dim _i(ids.Count - 1) As String
            Dim _f(ids.Count - 1) As String
            Dim _n(ids.Count - 1) As String
            Dim _c(ids.Count - 1) As String
            Dim _b(ids.Count - 1) As Double
            Dim _m(ids.Count - 1) As Double

            Array.Copy(ids.ToArray, _i, ids.Count)
            Array.Copy(formulas.ToArray, _f, ids.Count)
            Array.Copy(nms.ToArray, _n, ids.Count)
            Array.Copy(casnos.ToArray, _c, ids.Count)
            Array.Copy(bts.ToArray, _b, ids.Count)
            Array.Copy(molws.ToArray, _m, ids.Count)

            If ids.Count > 0 Then
                compIds = _i
                formulae = _f
                names = _n
                boilTemps = _b
                casNo = _c
                molWt = _m
            Else
                compIds = Nothing
                formulae = Nothing
                names = Nothing
                casNo = Nothing
                boilTemps = Nothing
                molWt = Nothing
            End If

        End Sub

        ''' <summary>
        ''' Provides the list of the supported phases. When supported for one or more property calculations, the Overall
        ''' phase and multiphase identifiers must be returned by this method.
        ''' </summary>
        ''' <returns>The list of phases supported by the Property Package.</returns>
        ''' <remarks></remarks>
        Public Overridable Function GetPhaseList() As Object Implements CapeOpen.ICapeThermoPropertyPackage.GetPhaseList
            Dim pl As New ArrayList
            For Each pi As PhaseInfo In Me.PhaseMappings.Values
                If pi.PhaseLabel <> "Disabled" Then
                    pl.Add(pi.PhaseLabel)
                End If
            Next
            pl.Add("Overall")
            Dim arr(pl.Count - 1) As String
            Array.Copy(pl.ToArray, arr, pl.Count)
            Return arr
        End Function

        ''' <summary>
        ''' Returns list of properties supported by the Property Package.
        ''' </summary>
        ''' <returns>List of all supported Properties.</returns>
        ''' <remarks>GetPropList should return identifiers for the non-constant properties calculated by CalcProp. Standard
        ''' identifiers are listed in 3.10.1. Other non-standard properties that are supported by the Property Package can
        ''' also be returned. GetPropList must not return identifiers for compound constant properties returned by
        ''' GetComponentConstant.
        ''' The properties temperature, pressure, fraction, flow, phaseFraction, totalFlow cannot be returned by
        ''' GetPropList, since all thermodynamic software components must support them. Although the property
        ''' identifier of derivative properties is formed from the identifier of another property, the GetPropList method
        ''' must return the identifiers of all supported derivative and non-derivative properties. For instance, a Property
        ''' Package could return the following list:
        ''' enthalpy, enthalpy.Dtemperature, entropy, entropy.Dpressure.</remarks>
        Public Overridable Function GetPropList() As Object Implements CapeOpen.ICapeThermoPropertyPackage.GetPropList
            Dim arr As New ArrayList
            With arr
                .Add("vaporPressure")
                .Add("surfaceTension")
                .Add("compressibilityFactor")
                .Add("heatOfVaporization")
                .Add("heatCapacity")
                .Add("heatCapacityCv")
                .Add("idealGasHeatCapacity")
                .Add("idealGasEnthalpy")
                .Add("excessEnthalpy")
                .Add("excessGibbsFreeEnergy")
                .Add("excessEntropy")
                .Add("viscosity")
                .Add("thermalConductivity")
                .Add("fugacity")
                .Add("fugacityCoefficient")
                .Add("activity")
                .Add("activityCoefficient")
                .Add("dewPointPressure")
                .Add("dewPointTemperature")
                .Add("kvalue")
                .Add("logFugacityCoefficient")
                .Add("logkvalue")
                .Add("volume")
                .Add("density")
                .Add("enthalpy")
                .Add("entropy")
                .Add("enthalpyF")
                .Add("entropyF")
                .Add("enthalpyNF")
                .Add("entropyNF")
                .Add("gibbsFreeEnergy")
                .Add("moles")
                .Add("mass")
                .Add("molecularWeight")
                .Add("boilingPointTemperature")
            End With

            Dim arr2(arr.Count) As String
            Array.Copy(arr.ToArray, arr2, arr.Count)
            Return arr2

        End Function

        ''' <summary>
        ''' Returns the values of the Universal Constants.
        ''' </summary>
        ''' <param name="materialObject">The Material Object.</param>
        ''' <param name="props">List of requested Universal Constants;</param>
        ''' <returns>Values of Universal Constants.</returns>
        ''' <remarks></remarks>
        Public Overridable Function GetUniversalConstant(ByVal materialObject As Object, ByVal props As Object) As Object Implements CapeOpen.ICapeThermoPropertyPackage.GetUniversalConstant
            Dim res As New ArrayList
            For Each p As String In props
                Select Case p.ToLower
                    Case "standardaccelerationofgravity"
                        res.Add(9.80665)
                    Case "avogadroconstant"
                        res.Add(6.0221419947E+23)
                    Case "boltzmannconstant"
                        res.Add(1.38065324E-23)
                    Case "molargasconstant"
                        res.Add(8.31447215)
                End Select
            Next

            Dim arr2(res.Count) As Double
            Array.Copy(res.ToArray, arr2, res.Count)
            Return arr2

        End Function

        ''' <summary>
        ''' Check to see if properties can be calculated.
        ''' </summary>
        ''' <param name="materialObject">The Material Object for the calculations.</param>
        ''' <param name="props">List of Properties to check.</param>
        ''' <returns>The array of booleans for each property.</returns>
        ''' <remarks>As it was unclear from the original specification what PropCheck should exactly be checking, and as the
        ''' argument list does not include a phase specification, implementations vary. It is generally expected that
        ''' PropCheck at least verifies that the Property is available for calculation in the property Package. However,
        ''' this can also be verified with PropList. It is advised not to use PropCheck.</remarks>
        Public Overridable Function PropCheck(ByVal materialObject As Object, ByVal props As Object) As Object Implements CapeOpen.ICapeThermoPropertyPackage.PropCheck
            Return True
        End Function

        ''' <summary>
        ''' Checks the validity of the calculation. This method is deprecated.
        ''' </summary>
        ''' <param name="materialObject">The Material Object for the calculations.</param>
        ''' <param name="props">The list of properties to check.</param>
        ''' <returns>The properties for which reliability is checked.</returns>
        ''' <remarks>The ValidityCheck method must not be used, since the ICapeThermoReliability interface is not yet defined.</remarks>
        Public Overridable Function ValidityCheck(ByVal materialObject As Object, ByVal props As Object) As Object Implements CapeOpen.ICapeThermoPropertyPackage.ValidityCheck
            Return True
        End Function

        ''' <summary>
        ''' Method responsible for calculating phase equilibria.
        ''' </summary>
        ''' <param name="materialObject">Material Object of the calculation</param>
        ''' <param name="flashType">Flash calculation type.</param>
        ''' <param name="props">Properties to be calculated at equilibrium. UNDEFINED for no
        ''' properties. If a list, then the property values should be set for each
        ''' phase present at equilibrium. (not including the overall phase).</param>
        ''' <remarks>The CalcEquilibrium method must set on the Material Object the amounts (phaseFraction) and compositions
        ''' (fraction) for all phases present at equilibrium, as well as the temperature and pressure for the overall
        ''' mixture, if not set as part of the calculation specifications. The CalcEquilibrium method must not set on the
        ''' Material Object any other value - in particular it must not set any values for phases that do not exist. See
        ''' 5.2.1 for more information.
        ''' The available list of flashes is given in section 5.6.1.
        ''' It is advised not to combine a flash calculation with a property calculation. By the returned error one cannot
        ''' see which has failed, plus the additional arguments to CalcProp (such as calculation type) cannot be
        ''' specified. Advice is to perform a CalcEquilibrium, get the phaseIDs and perform a CalcProp for those
        ''' phases.</remarks>
        Public Overridable Sub CalcEquilibrium2(ByVal materialObject As Object, ByVal flashType As String, ByVal props As Object) Implements CapeOpen.ICapeThermoEquilibriumServer.CalcEquilibrium
            CalcEquilibrium(materialObject, flashType, props)
        End Sub

        ''' <summary>
        ''' Checks to see if a given type of flash calculations can be performed and whether the properties can be
        ''' calculated after the flash calculation.
        ''' </summary>
        ''' <param name="materialObject">The Material Object for the calculations.</param>
        ''' <param name="flashType">Type of flash calculation to check</param>
        ''' <param name="props">List of Properties to check. UNDEFINED for none.</param>
        ''' <param name="valid">The array of booleans for flash and property. First element is reserved for flashType.</param>
        ''' <remarks>As it was unclear from the original specification what PropCheck should exactly be checking, and as the
        ''' argument list does not include a phase specification, implementations vary. It is generally expected that
        ''' PropCheck at least verifies that the Property is available for calculation in the Material Object. However, this
        ''' can also be verified with PropList. It is advised not to use PropCheck.
        ''' DWSIM doesn't implement this interface.</remarks>
        Public Overridable Sub PropCheck1(ByVal materialObject As Object, ByVal flashType As String, ByVal props As Object, ByRef valid As Object) Implements CapeOpen.ICapeThermoEquilibriumServer.PropCheck
            Throw New CapeOpen.CapeNoImplException
        End Sub

        ''' <summary>
        ''' Returns the flash types, properties, phases, and calculation types that are supported by a given Equilibrium Server Routine.
        ''' </summary>
        ''' <param name="flashType">Type of flash calculations supported.</param>
        ''' <param name="props">List of supported properties.</param>
        ''' <param name="phases">List of supported phases.</param>
        ''' <param name="calcType">List of supported calculation types.</param>
        ''' <remarks></remarks>
        Public Overridable Sub PropList(ByRef flashType As Object, ByRef props As Object, ByRef phases As Object, ByRef calcType As Object) Implements CapeOpen.ICapeThermoEquilibriumServer.PropList
            props = GetPropList()
            flashType = New String() {"TP", "PH", "PS", "TVF", "PVF", "PT", "HP", "SP", "VFT", "VFP"}
            phases = New String() {"Vapor", "Liquid1", "Liquid2", "Solid", "Overall"}
            calcType = New String() {"Mixture"}
        End Sub

        Public Overridable Sub ValidityCheck1(ByVal materialObject As Object, ByVal props As Object, ByRef relList As Object) Implements CapeOpen.ICapeThermoEquilibriumServer.ValidityCheck
            Throw New CapeOpen.CapeNoImplException
        End Sub

        Public Overridable Sub CalcProp1(ByVal materialObject As Object, ByVal props As Object, ByVal phases As Object, ByVal calcType As String) Implements CapeOpen.ICapeThermoCalculationRoutine.CalcProp
            CalcProp(materialObject, props, phases, calcType)
        End Sub

        Public Overridable Function GetPropList1() As Object Implements CapeOpen.ICapeThermoCalculationRoutine.GetPropList
            Return GetPropList()
        End Function

        Public Overridable Function PropCheck2(ByVal materialObject As Object, ByVal props As Object) As Object Implements CapeOpen.ICapeThermoCalculationRoutine.PropCheck
            Return True
        End Function

        Public Overridable Function ValidityCheck2(ByVal materialObject As Object, ByVal props As Object) As Object Implements CapeOpen.ICapeThermoCalculationRoutine.ValidityCheck
            Return True
        End Function

#End Region

#Region "   CAPE-OPEN 1.1 Thermo & Physical Properties"

        ''' <summary>
        ''' Returns the values of constant Physical Properties for the specified Compounds.
        ''' </summary>
        ''' <param name="props">The list of Physical Property identifiers. Valid
        ''' identifiers for constant Physical Properties are listed in section 7.5.2.</param>
        ''' <param name="compIds">List of Compound identifiers for which constants are to
        ''' be retrieved. Set compIds to UNDEFINED to denote all Compounds in the component that implements the ICapeThermoCompounds interface.</param>
        ''' <returns>Values of constants for the specified Compounds.</returns>
        ''' <remarks>The GetConstPropList method can be used in order to check which constant Physical
        ''' Properties are available.
        ''' If the number of requested Physical Properties is P and the number of Compounds is C, the
        ''' propvals array will contain C*P variants. The first C variants will be the values for the first
        ''' requested Physical Property (one variant for each Compound) followed by C values of constants
        ''' for the second Physical Property, and so on. The actual type of values returned
        ''' (Double, String, etc.) depends on the Physical Property as specified in section 7.5.2.
        ''' Physical Properties are returned in a fixed set of units as specified in section 7.5.2.
        ''' If the compIds argument is set to UNDEFINED this is a request to return property values for
        ''' all compounds in the component that implements the ICapeThermoCompounds interface
        ''' with the compound order the same as that returned by the GetCompoundList method. For
        ''' example, if the interface is implemented by a Property Package component the property
        ''' request with compIds set to UNDEFINED means all compounds in the Property Package
        ''' rather than all compounds in the Material Object passed to the Property package.
        ''' If any Physical Property is not available for one or more Compounds, then undefined values
        ''' must be returned for those combinations and an ECapeThrmPropertyNotAvailable exception
        ''' must be raised. If the exception is raised, the client should check all the values returned to
        ''' determine which is undefined.</remarks>
        Public Overridable Function GetCompoundConstant(ByVal props As Object, ByVal compIds As Object) As Object Implements ICapeThermoCompounds.GetCompoundConstant
            Dim vals As New ArrayList
            For Each s As String In compIds
                Dim c As Interfaces.ICompound = Me.CurrentMaterialStream.Phases(0).Compounds(s)
                For Each p As String In props
                    Select Case p.ToLower
                        Case "molecularweight"
                            vals.Add(c.ConstantProperties.Molar_Weight)
                        Case "criticaltemperature"
                            vals.Add(c.ConstantProperties.Critical_Temperature)
                        Case "criticalpressure"
                            vals.Add(c.ConstantProperties.Critical_Pressure)
                        Case "criticalvolume"
                            vals.Add(c.ConstantProperties.Critical_Volume / 1000)
                        Case "criticalcompressibilityfactor"
                            vals.Add(c.ConstantProperties.Critical_Compressibility)
                        Case "acentricfactor"
                            vals.Add(c.ConstantProperties.Acentric_Factor)
                        Case "normalboilingpoint"
                            vals.Add(c.ConstantProperties.Normal_Boiling_Point)
                        Case "idealgasgibbsfreeenergyofformationat25c"
                            vals.Add(c.ConstantProperties.IG_Gibbs_Energy_of_Formation_25C * c.ConstantProperties.Molar_Weight)
                        Case "idealgasenthalpyofformationat25c"
                            vals.Add(c.ConstantProperties.IG_Enthalpy_of_Formation_25C * c.ConstantProperties.Molar_Weight)
                        Case "casregistrynumber"
                            vals.Add(c.ConstantProperties.CAS_Number)
                        Case "chemicalformula", "structureformula"
                            vals.Add(c.ConstantProperties.Formula)
                        Case Else
                            Throw New CapeOpen.CapeNoImplException
                    End Select
                Next
            Next
            Dim arr2(vals.Count - 1) As Object
            Array.Copy(vals.ToArray, arr2, vals.Count)
            Return arr2
        End Function

        ''' <summary>
        ''' Returns the list of all Compounds. This includes the Compound identifiers recognised and extra
        '''information that can be used to further identify the Compounds.
        ''' </summary>
        ''' <param name="compIds">List of Compound identifiers</param>
        ''' <param name="formulae">List of Compound formulae</param>
        ''' <param name="names">List of Compound names.</param>
        ''' <param name="boilTemps">List of boiling point temperatures.</param>
        ''' <param name="molwts">List of molecular weights.</param>
        ''' <param name="casnos">List of Chemical Abstract Service (CAS) Registry numbers.</param>
        ''' <remarks>If any item cannot be returned then the value should be set to UNDEFINED. The same information
        ''' can also be extracted using the GetCompoundConstant method. The equivalences
        ''' between GetCompoundList arguments and Compound constant Physical Properties, as
        ''' specified in section 7.5.2, is given in the table below.
        ''' When the ICapeThermoCompounds interface is implemented by a Material Object, the list
        ''' of Compounds returned is fixed when the Material Object is configured.
        ''' For a Property Package component, the Property Package will normally contain a limited set
        ''' of Compounds selected for a particular application, rather than all possible Compounds that
        ''' could be available to a proprietary Properties System.
        ''' The compIds returned by the GetCompoundList method must be unique within the
        ''' component that implements the ICapeThermoCompounds interface. There is no restriction
        ''' on the length of the strings returned in compIds. However, it should be recognised that a
        ''' PME may restrict the length of Compound identifiers internally. In such a case the PME’s
        ''' CAPE-OPEN socket must maintain a method of mapping the, potentially long, identifiers
        ''' used by a CAPE-OPEN Property package component to the identifiers used within the PME.
        ''' In order to identify the Compounds of a Property Package, the PME, or other client, will use
        ''' the casnos argument rather than the compIds. This is because different PMEs and different
        ''' Property Packages may give different names to the same Compounds and the casnos is
        ''' (almost always) unique. If the casnos is not available (e.g. for petroleum fractions), or not
        ''' unique, the other pieces of information returned by GetCompoundList can be used to
        ''' distinguish the Compounds. It should be noted, however, that for communication with a
        ''' Property Package a client must use the Compound identifiers returned in the compIds
        ''' argument. It is the responsibility of the client to maintain appropriate data structures that
        ''' allow it to reconcile the different Compound identifiers used by different Property Packages
        ''' and any native property system.</remarks>
        Public Overridable Sub GetCompoundList(ByRef compIds As Object, ByRef formulae As Object, ByRef names As Object, ByRef boilTemps As Object, ByRef molwts As Object, ByRef casnos As Object) Implements ICapeThermoCompounds.GetCompoundList
            GetComponentList(compIds, formulae, names, boilTemps, molwts, casnos)
        End Sub

        ''' <summary>
        ''' Returns the list of supported constant Physical Properties.
        ''' </summary>
        ''' <returns>List of identifiers for all supported constant Physical Properties. The standard constant property identifiers are listed in section 7.5.2.</returns>
        ''' <remarks>GetConstPropList returns identifiers for all the constant Physical Properties that can be
        ''' retrieved by the GetCompoundConstant method. If no properties are supported,
        ''' UNDEFINED should be returned. The CAPE-OPEN standards do not define a minimum list
        ''' of Physical Properties to be made available by a software component that implements the
        ''' ICapeThermoCompounds interface.
        ''' A component that implements the ICapeThermoCompounds interface may return constant
        ''' Physical Property identifiers which do not belong to the list defined in section 7.5.2.
        ''' However, these proprietary identifiers may not be understood by most of the clients of this
        ''' component.</remarks>
        Public Overridable Function GetConstPropList() As Object Implements ICapeThermoCompounds.GetConstPropList
            Dim vals As New ArrayList
            With vals
                .Add("molecularweight")
                .Add("criticaltemperature")
                .Add("criticalpressure")
                .Add("criticalvolume")
                .Add("criticalcompressibilityfactor")
                .Add("acentricfactor")
                .Add("normalboilingpoint")
                .Add("idealgasgibbsfreeenergyofformationat25c")
                .Add("idealgasenthalpyofformationat25c")
                .Add("casregistrynumber")
                .Add("chemicalformula")
            End With
            Dim arr2(vals.Count - 1) As String
            Array.Copy(vals.ToArray, arr2, vals.Count)
            Return arr2
        End Function

        ''' <summary>
        ''' Returns the number of Compounds supported.
        ''' </summary>
        ''' <returns>Number of Compounds supported.</returns>
        ''' <remarks>The number of Compounds returned by this method must be equal to the number of
        ''' Compound identifiers that are returned by the GetCompoundList method of this interface. It
        ''' must be zero or a positive number.</remarks>
        Public Overridable Function GetNumCompounds() As Integer Implements ICapeThermoCompounds.GetNumCompounds
            If Calculator.CAPEOPENMode Then
                Return Me._selectedcomps.Count
            Else
                Return Me.CurrentMaterialStream.Phases(0).Compounds.Count
            End If
        End Function

        ''' <summary>
        ''' Returns the values of pressure-dependent Physical Properties for the specified pure Compounds.
        ''' </summary>
        ''' <param name="props">The list of Physical Property identifiers. Valid identifiers for pressure-dependent 
        ''' Physical Properties are listed in section 7.5.4</param>
        ''' <param name="pressure">Pressure (in Pa) at which Physical Properties are evaluated</param>
        ''' <param name="compIds">List of Compound identifiers for which Physical Properties are to be retrieved. 
        ''' Set compIds to UNDEFINED to denote all Compounds in the component that implements the ICapeThermoCompounds interface.</param>
        ''' <param name="propVals">Property values for the Compounds specified.</param>
        ''' <remarks></remarks>
        Public Overridable Sub GetPDependentProperty(ByVal props As Object, ByVal pressure As Double, ByVal compIds As Object, ByRef propVals As Object) Implements ICapeThermoCompounds.GetPDependentProperty
            Dim vals As New ArrayList
            For Each c As String In compIds
                For Each p As String In props
                    Select Case p.ToLower
                        Case "boilingpointtemperature"
                            vals.Add(Me.AUX_TSATi(pressure, c))
                    End Select
                Next
            Next
            Dim arr2(vals.Count - 1) As Double
            Array.Copy(vals.ToArray, arr2, vals.Count)
            propVals = arr2
        End Sub

        ''' <summary>
        ''' Returns the list of supported pressure-dependent properties.
        ''' </summary>
        ''' <returns>The list of Physical Property identifiers for all supported pressure-dependent properties. The standard identifiers are listed in section 7.5.4</returns>
        ''' <remarks>GetPDependentPropList returns identifiers for all the pressure-dependent properties that can
        ''' be retrieved by the GetPDependentProperty method. If no properties are supported
        ''' UNDEFINED should be returned. The CAPE-OPEN standards do not define a minimum list
        ''' of Physical Properties to be made available by a software component that implements the
        ''' ICapeThermoCompounds interface.
        ''' A component that implements the ICapeThermoCompounds interface may return identifiers
        ''' which do not belong to the list defined in section 7.5.4. However, these proprietary
        ''' identifiers may not be understood by most of the clients of this component.</remarks>
        Public Overridable Function GetPDependentPropList() As Object Implements ICapeThermoCompounds.GetPDependentPropList
            Return New String() {"boilingPointTemperature"}
        End Function

        ''' <summary>
        ''' Returns the values of temperature-dependent Physical Properties for the specified pure Compounds.
        ''' </summary>
        ''' <param name="props">The list of Physical Property identifiers. Valid identifiers for 
        ''' temperature-dependent Physical Properties are listed in section 7.5.3</param>
        ''' <param name="temperature">Temperature (in K) at which properties are evaluated</param>
        ''' <param name="compIds">List of Compound identifiers for which Physical Properties are to be retrieved. 
        ''' Set compIds to UNDEFINED to denote all Compounds in the component that implements the ICapeThermoCompounds interface.</param>
        ''' <param name="propVals">Physical Property values for the Compounds specified.</param>
        ''' <remarks>The GetTDependentPropList method can be used in order to check which Physical
        ''' Properties are available.
        ''' If the number of requested Physical Properties is P and the number of Compounds is C, the
        ''' propvals array will contain C*P values. The first C will be the values for the first requested
        ''' Physical Property followed by C values for the second Physical Property, and so on.
        ''' Properties are returned in a fixed set of units as specified in section 7.5.3.
        ''' If the compIds argument is set to UNDEFINED this is a request to return property values for
        ''' all compounds in the component that implements the ICapeThermoCompounds interface
        ''' with the compound order the same as that returned by the GetCompoundList method. For
        ''' example, if the interface is implemented by a Property Package component the property
        ''' request with compIds set to UNDEFINED means all compounds in the Property Package
        ''' rather than all compounds in the Material Object passed to the Property package.
        ''' If any Physical Property is not available for one or more Compounds, then undefined values
        ''' must be returned for those combinations and an ECapeThrmPropertyNotAvailable exception
        ''' must be raised. If the exception is raised, the client should check all the values returned to
        ''' determine which is undefined.</remarks>
        Public Overridable Sub GetTDependentProperty(ByVal props As Object, ByVal temperature As Double, ByVal compIds As Object, ByRef propVals As Object) Implements ICapeThermoCompounds.GetTDependentProperty
            Dim vals As New ArrayList
            For Each c As String In compIds
                For Each p As String In props
                    Select Case p.ToLower
                        Case "heatofvaporization"
                            vals.Add(Me.AUX_HVAPi(c, temperature) * Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Molar_Weight)
                        Case "idealgasenthalpy"
                            vals.Add(Me.RET_Hid_i(298.15, temperature, c) * Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Molar_Weight)
                        Case "idealgasentropy"
                            vals.Add(Me.RET_Sid_i(298.15, temperature, 101325, c) * Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Molar_Weight)
                        Case "idealgasheatcapacity"
                            vals.Add(Me.AUX_CPi(c, temperature) * Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Molar_Weight)
                        Case "vaporpressure"
                            vals.Add(Me.AUX_PVAPi(c, temperature))
                        Case "viscosityofliquid"
                            vals.Add(Me.AUX_LIQVISCi(c, temperature))
                        Case "heatcapacityofliquid"
                            vals.Add(Me.AUX_LIQ_Cpi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature) * Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Molar_Weight)
                        Case "heatcapacityofsolid"
                            vals.Add(Me.AUX_SolidHeatCapacity(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature) * Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Molar_Weight)
                        Case "thermalconductivityofliquid"
                            vals.Add(Me.AUX_LIQTHERMCONDi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature))
                        Case "thermalconductivityofvapor"
                            vals.Add(Me.AUX_VAPTHERMCONDi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature, Me.AUX_PVAPi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties.Name, temperature)))
                        Case "viscosityofvapor"
                            vals.Add(Me.AUX_VAPVISCi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature))
                        Case "densityofliquid"
                            vals.Add(Me.AUX_LIQDENSi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature))
                        Case "densityofsolid"
                            vals.Add(Me.AUX_SOLIDDENSi(Me.CurrentMaterialStream.Phases(0).Compounds(c).ConstantProperties, temperature))
                    End Select
                Next
            Next
            Dim arr2(vals.Count - 1) As Double
            Array.Copy(vals.ToArray, arr2, vals.Count)
            propVals = arr2
        End Sub

        ''' <summary>
        ''' Returns the list of supported temperature-dependent Physical Properties.
        ''' </summary>
        ''' <returns>The list of Physical Property identifiers for all supported temperature-dependent 
        ''' properties. The standard identifiers are listed in section 7.5.3</returns>
        ''' <remarks>GetTDependentPropList returns identifiers for all the temperature-dependent Physical
        ''' Properties that can be retrieved by the GetTDependentProperty method. If no properties are
        ''' supported UNDEFINED should be returned. The CAPE-OPEN standards do not define a
        ''' minimum list of properties to be made available by a software component that implements
        ''' the ICapeThermoCompounds interface.
        ''' A component that implements the ICapeThermoCompounds interface may return identifiers
        ''' which do not belong to the list defined in section 7.5.3. However, these proprietary identifiers
        ''' may not be understood by most of the clients of this component.</remarks>
        Public Overridable Function GetTDependentPropList() As Object Implements ICapeThermoCompounds.GetTDependentPropList
            Dim vals As New ArrayList
            With vals

                .Add("heatOfVaporization")
                .Add("idealGasEnthalpy")
                .Add("idealGasEntropy")
                .Add("idealGasHeatCapacity")
                .Add("vaporPressure")
                .Add("viscosityOfLiquid")

                .Add("heatCapacityOfLiquid")
                .Add("heatCapacityOfSolid")
                .Add("thermalConductivityOfLiquid")
                .Add("thermalConductivityOfVapor")
                .Add("viscosityOfVapor")

                .Add("densityOfLiquid")
                .Add("densityOfSolid")

            End With
            Dim arr2(vals.Count - 1) As String
            Array.Copy(vals.ToArray, arr2, vals.Count)
            Return arr2
        End Function

        Public Overridable Function GetNumPhases() As Integer Implements ICapeThermoPhases.GetNumPhases
            Dim i As Integer = 0
            For Each pi As PhaseInfo In Me.PhaseMappings.Values
                If pi.PhaseLabel <> "Disabled" Then
                    i += 1
                End If
            Next
            Return i
        End Function

        ''' <summary>
        ''' Returns information on an attribute associated with a Phase for the purpose of understanding 
        ''' what lies behind a Phase label.
        ''' </summary>
        ''' <param name="phaseLabel">A (single) Phase label. This must be one of the values returned by GetPhaseList method.</param>
        ''' <param name="phaseAttribute">One of the Phase attribute identifiers from the table below.</param>
        ''' <returns>The value corresponding to the Phase attribute identifier – see table below.</returns>
        ''' <remarks>GetPhaseInfo is intended to allow a PME, or other client, to identify a Phase with an arbitrary
        ''' label. A PME, or other client, will need to do this to map stream data into a Material
        ''' Object, or when importing a Property Package. If the client cannot identify the Phase, it can
        ''' ask the user to provide a mapping based on the values of these properties.
        ''' The list of supported Phase attributes is defined in the following table:
        ''' 
        ''' Phase attribute identifier            Supported values
        ''' 
        ''' StateOfAggregation                    One of the following strings:
        '''                                       Vapor
        '''                                       Liquid
        '''                                       Solid
        '''                                       Unknown
        ''' 
        ''' KeyCompoundId                         The identifier of the Compound (compId as returned by GetCompoundList) 
        '''                                       that is expected to be present in highest concentration in the Phase. 
        '''                                       May be undefined in which case UNDEFINED should be returned.
        ''' 
        ''' ExcludedCompoundId                    The identifier of the Compound (compId as returned by
        '''                                       GetCompoundList) that is expected to be present in low or zero
        '''                                       concentration in the Phase. May not be defined in which case
        '''                                       UNDEFINED should be returned.
        ''' 
        ''' DensityDescription                    A description that indicates the density range expected for the Phase.
        '''                                       One of the following strings or UNDEFINED:
        '''                                       Heavy
        '''                                       Light
        ''' 
        ''' UserDescription                       A description that helps the user or PME to identify the Phase.
        '''                                       It can be any string or UNDEFINED.
        ''' 
        ''' TypeOfSolid                           A description that provides more information about a solid Phase. For
        '''                                       Phases with a “Solid” state of aggregation it may be one of the
        '''                                       following standard strings or UNDEFINED:
        '''                                       PureSolid
        '''                                       SolidSolution
        '''                                       HydrateI
        '''                                       HydrateII
        '''                                       HydrateH
        '''                                       Other values may be returned for solid Phases but these may not be
        '''                                       understood by most clients.
        '''                                       For Phases with any other state of aggregation it must be
        '''                                       UNDEFINED.</remarks>
        Public Overridable Function GetPhaseInfo(ByVal phaseLabel As String, ByVal phaseAttribute As String) As Object Implements ICapeThermoPhases.GetPhaseInfo
            Dim retval As Object = Nothing
            Select Case phaseLabel
                Case "Vapor"
                    Select Case phaseAttribute
                        Case "StateOfAggregation"
                            retval = "Vapor"
                        Case "keyCompoundId"
                            retval = Nothing
                        Case "ExcludedCompoundId"
                            retval = Nothing
                        Case "DensityDescription"
                            retval = Nothing
                        Case "UserDescription"
                            retval = "Vapor Phase"
                        Case Else
                            retval = Nothing
                    End Select
                Case "Liquid1", "Liquid2", "Liquid3", "Liquid", "Aqueous"
                    Select Case phaseAttribute
                        Case "StateOfAggregation"
                            retval = "Liquid"
                        Case "keyCompoundId"
                            retval = Nothing
                        Case "ExcludedCompoundId"
                            retval = Nothing
                        Case "DensityDescription"
                            retval = Nothing
                        Case "UserDescription"
                            retval = "Liquid Phase"
                        Case Else
                            retval = Nothing
                    End Select
                Case "Solid"
                    Select Case phaseAttribute
                        Case "StateOfAggregation"
                            retval = "Solid"
                        Case "TypeOfSolid"
                            retval = "SolidSolution"
                        Case "keyCompoundId"
                            retval = Nothing
                        Case "ExcludedCompoundId"
                            retval = Nothing
                        Case "DensityDescription"
                            retval = Nothing
                        Case "UserDescription"
                            retval = "Solid Phase"
                        Case Else
                            retval = Nothing
                    End Select
            End Select
            Return retval
        End Function

        ''' <summary>
        ''' Returns Phase labels and other important descriptive information for all the Phases supported.
        ''' </summary>
        ''' <param name="phaseLabels">he list of Phase labels for the Phases supported. A Phase label can 
        ''' be any string but each Phase must have a unique label. If, for some reason, no Phases are 
        ''' supported an UNDEFINED value should be returned for the phaseLabels. The number of Phase labels 
        ''' must also be equal to the number of Phases returned by the GetNumPhases method.</param>
        ''' <param name="stateOfAggregation">The physical State of Aggregation associated with each of the 
        ''' Phases. This must be one of the following strings: ”Vapor”, “Liquid”, “Solid” or “Unknown”. Each 
        ''' Phase must have a single State of Aggregation. The value must not be left undefined, but may be 
        ''' set to “Unknown”.</param>
        ''' <param name="keyCompoundId">The key Compound for the Phase. This must be the Compound identifier 
        ''' (as returned by GetCompoundList), or it may be undefined in which case a UNDEFINED value is returned. 
        ''' The key Compound is an indication of the Compound that is expected to be present in high concentration 
        ''' in the Phase, e.g. water for an aqueous liquid phase. Each Phase can have a single key Compound.</param>
        ''' <remarks>The Phase label allows the phase to be uniquely identified in methods of the ICapeThermo-
        ''' Phases interface and other CAPE-OPEN interfaces. The State of Aggregation and key
        ''' Compound provide a way for the PME, or other client, to interpret the meaning of a Phase
        ''' label in terms of the physical characteristics of the Phase.
        ''' All arrays returned by this method must be of the same length, i.e. equal to the number of
        ''' Phase labels.
        ''' To get further information about a Phase, use the GetPhaseInfo method.</remarks>
        Public Overridable Sub GetPhaseList1(ByRef phaseLabels As Object, ByRef stateOfAggregation As Object, ByRef keyCompoundId As Object) Implements ICapeThermoPhases.GetPhaseList
            Dim pl, sa, kci As New ArrayList, tmpstr As Object
            For Each pin As PhaseInfo In Me.PhaseMappings.Values
                If pin.PhaseLabel <> "Disabled" Then
                    pl.Add(pin.PhaseLabel)
                    tmpstr = Me.GetPhaseInfo(pin.PhaseLabel, "StateOfAggregation")
                    sa.Add(tmpstr)
                    tmpstr = Me.GetPhaseInfo(pin.PhaseLabel, "keyCompoundId")
                    kci.Add(tmpstr)
                End If
            Next
            Dim myarr1(pl.Count - 1), myarr2(pl.Count - 1), myarr3(pl.Count - 1) As String
            Array.Copy(pl.ToArray, myarr1, pl.Count)
            Array.Copy(sa.ToArray, myarr2, pl.Count)
            Array.Copy(kci.ToArray, myarr3, pl.Count)
            phaseLabels = myarr1
            stateOfAggregation = myarr2
            keyCompoundId = myarr3
        End Sub

        ''' <summary>
        ''' This method is used to calculate the natural logarithm of the fugacity coefficients (and
        ''' optionally their derivatives) in a single Phase mixture. The values of temperature, pressure
        ''' and composition are specified in the argument list and the results are also returned through
        ''' the argument list.
        ''' </summary>
        ''' <param name="phaseLabel">Phase label of the Phase for which the properties are to be calculated. 
        ''' The Phase label must be one of the strings returned by the GetPhaseList method on the ICapeThermoPhases interface.</param>
        ''' <param name="temperature">The temperature (K) for the calculation.</param>
        ''' <param name="pressure">The pressure (Pa) for the calculation.</param>
        ''' <param name="moleNumbers">Number of moles of each Compound in the mixture.</param>
        ''' <param name="fFlags">Code indicating whether natural logarithm of the fugacity coefficients and/or derivatives 
        ''' should be calculated (see notes).</param>
        ''' <param name="lnPhi">Natural logarithm of the fugacity coefficients (if requested).</param>
        ''' <param name="lnPhiDT">Derivatives of natural logarithm of the fugacity coefficients w.r.t. temperature (if requested).</param>
        ''' <param name="lnPhiDP">Derivatives of natural logarithm of the fugacity coefficients w.r.t. pressure (if requested).</param>
        ''' <param name="lnPhiDn">Derivatives of natural logarithm of the fugacity coefficients w.r.t. mole numbers (if requested).</param>
        ''' <remarks>This method is provided to allow the natural logarithm of the fugacity coefficient, which is
        ''' the most commonly used thermodynamic property, to be calculated and returned in a highly
        ''' efficient manner.
        ''' The temperature, pressure and composition (mole numbers) for the calculation are specified
        ''' by the arguments and are not obtained from the Material Object by a separate request. Likewise,
        ''' any quantities calculated are returned through the arguments and are not stored in the
        ''' Material Object. The state of the Material Object is not affected by calling this method. It
        ''' should be noted however, that prior to calling CalcAndGetLnPhi a valid Material Object
        ''' must have been defined by calling the SetMaterial method on the
        ''' ICapeThermoMaterialContext interface of the component that implements the
        ''' ICapeThermoPropertyRoutine interface. The compounds in the Material Object must have
        ''' been identified and the number of values supplied in the moleNumbers argument must be
        ''' equal to the number of Compounds in the Material Object.
        ''' The fugacity coefficient information is returned as the natural logarithm of the fugacity
        ''' coefficient. This is because thermodynamic models naturally provide the natural logarithm
        ''' of this quantity and also a wider range of values may be safely returned.
        ''' The quantities actually calculated and returned by this method are controlled by an integer
        ''' code fFlags. The code is formed by summing contributions for the property and each
        ''' derivative required using the enumerated constants eCapeCalculationCode (defined in the
        ''' Thermo version 1.1 IDL) shown in the following table. For example, to calculate log
        ''' fugacity coefficients and their T-derivatives the fFlags argument would be set to
        ''' CAPE_LOG_FUGACITY_COEFFICIENTS + CAPE_T_DERIVATIVE.
        ''' 
        '''                                       code                            numerical value
        ''' no calculation                        CAPE_NO_CALCULATION             0
        ''' log fugacity coefficients             CAPE_LOG_FUGACITY_COEFFICIENTS  1
        ''' T-derivative                          CAPE_T_DERIVATIVE               2
        ''' P-derivative                          CAPE_P_DERIVATIVE               4
        ''' mole number derivatives               CAPE_MOLE_NUMBERS_DERIVATIVES   8
        ''' 
        ''' If CalcAndGetLnPhi is called with fFlags set to CAPE_NO_CALCULATION no property
        ''' values are returned.
        ''' A typical sequence of operations for this method when implemented by a Property Package
        ''' component would be:
        ''' - Check that the phaseLabel specified is valid.
        ''' - Check that the moleNumbers array contains the number of values expected
        ''' (should be consistent with the last call to the SetMaterial method).
        ''' - Calculate the requested properties/derivatives at the T/P/composition specified in
        ''' the argument list.
        ''' - Store values for the properties/derivatives in the corresponding arguments.
        ''' Note that this calculation can be carried out irrespective of whether the Phase actually exists
        ''' in the Material Object.</remarks>
        Public Overridable Sub CalcAndGetLnPhi(ByVal phaseLabel As String, ByVal temperature As Double, ByVal pressure As Double, ByVal moleNumbers As Object, ByVal fFlags As Integer, ByRef lnPhi As Object, ByRef lnPhiDT As Object, ByRef lnPhiDP As Object, ByRef lnPhiDn As Object) Implements ICapeThermoPropertyRoutine.CalcAndGetLnPhi
            Select Case fFlags
                Case CapeCalculationCode.CAPE_LOG_FUGACITY_COEFFICIENTS
                    'normalize mole fractions
                    Dim tmols As Double, Vx(moleNumbers.length) As Double
                    For i As Integer = 0 To moleNumbers.length - 1
                        tmols += moleNumbers(i)
                    Next
                    For i As Integer = 0 To moleNumbers.length - 1
                        moleNumbers(i) /= tmols
                    Next
                    Select Case phaseLabel
                        Case "Vapor"
                            lnPhi = Me.DW_CalcFugCoeff(Vx, temperature, pressure, State.Vapor)
                        Case "Liquid"
                            lnPhi = Me.DW_CalcFugCoeff(Vx, temperature, pressure, State.Liquid)
                        Case "Solid"
                            lnPhi = Me.DW_CalcFugCoeff(Vx, temperature, pressure, State.Solid)
                    End Select
                    For i As Integer = 0 To moleNumbers.length - 1
                        lnPhi(i) = Log(lnPhi(i))
                    Next
                Case Else
                    Throw New CapeOpen.CapeThrmPropertyNotAvailableException
            End Select
        End Sub

        ''' <summary>
        ''' CalcSinglePhaseProp is used to calculate properties and property derivatives of a mixture in
        ''' a single Phase at the current values of temperature, pressure and composition set in the
        ''' Material Object. CalcSinglePhaseProp does not perform phase Equilibrium Calculations.
        ''' </summary>
        ''' <param name="props">The list of identifiers for the single-phase properties or derivatives to 
        ''' be calculated. See sections 7.5.5 and 7.6 for the standard identifiers.</param>
        ''' <param name="phaseLabel">Phase label of the Phase for which the properties are to be calculated. 
        ''' The Phase label must be one of the strings returned by the GetPhaseList method on the 
        ''' ICapeThermoPhases interface and the phase must be present in the Material Object.</param>
        ''' <remarks>CalcSinglePhaseProp calculates properties, such as enthalpy or viscosity that are defined for
        ''' a single Phase. Physical Properties that depend on more than one Phase, for example surface
        ''' tension or K-values, are handled by CalcTwoPhaseProp method.
        ''' Components that implement this method must get the input specification for the calculation
        ''' (temperature, pressure and composition) from the associated Material Object and set the
        ''' results in the Material Object.
        ''' Thermodynamic and Physical Properties Components, such as a Property Package or Property
        ''' Calculator, must implement the ICapeThermoMaterialContext interface so that an
        ''' ICapeThermoMaterial interface can be passed via the SetMaterial method.
        ''' The component that implements the ICapeThermoPropertyRoutine interface (e.g. a Property
        ''' Package or Property Calculator) must also implement the ICapeThermoPhases interface so
        ''' that it is possible to get a list of supported phases. The phaseLabel passed to this method
        ''' must be one of the phase labels returned by the GetPhaseList method of the
        ''' ICapeThermoPhases interface and it must also be present in the Material Object, ie. one of
        ''' the phase labels returned by the GetPresentPhases method of the ICapeThermoMaterial
        ''' interface. This latter condition will be satisfied if the phase is made present explicitly by
        ''' calling the SetPresentPhases method or if any phase properties have been set by calling the
        ''' SetSinglePhaseProp or SetTwoPhaseProp methods.
        ''' A typical sequence of operations for CalcSinglePhaseProp when implemented by a Property
        ''' Package component would be:
        ''' - Check that the phaseLabel specified is valid.
        ''' - Use the GetTPFraction method (of the Material Object specified in the last call to the
        ''' SetMaterial method) to get the temperature, pressure and composition of the
        ''' specified Phase.
        ''' - Calculate the properties.
        ''' - Store values for the properties of the Phase in the Material Object using the
        ''' SetSinglePhaseProp method of the ICapeThermoMaterial interface.
        ''' CalcSinglePhaseProp will request the input Property values it requires from the Material
        ''' Object through GetSinglePhaseProp calls. If a requested property is not available, the
        ''' exception raised will be ECapeThrmPropertyNotAvailable. If this error occurs then the
        ''' Property Package can return it to the client, or request a different property. Material Object
        ''' implementations must be able to supply property values using the client’s choice of basis by
        ''' implementing conversion from one basis to another.
        ''' Clients should not assume that Phase fractions and Compound fractions in a Material Object
        ''' are normalised. Fraction values may also lie outside the range 0 to 1. If fractions are not
        ''' normalised, or are outside the expected range, it is the responsibility of the Property Package
        ''' to decide how to deal with the situation.
        ''' It is recommended that properties are requested one at a time in order to simplify error
        ''' handling. However, it is recognised that there are cases where the potential efficiency gains
        ''' of requesting several properties simultaneously are more important. One such example
        ''' might be when a property and its derivatives are required.
        ''' If a client uses multiple properties in a call and one of them fails then the whole call should
        ''' be considered to have failed. This implies that no value should be written back to the Material
        ''' Object by the Property Package until it is known that the whole request can be satisfied.
        ''' It is likely that a PME might request values of properties for a Phase at conditions of temperature,
        ''' pressure and composition where the Phase does not exist (according to the
        ''' mathematical/physical models used to represent properties). The exception
        ''' ECapeThrmPropertyNotAvailable may be raised or an extrapolated value may be returned.
        ''' It is responsibility of the implementer to decide how to handle this circumstance.</remarks>
        Public Overridable Sub CalcSinglePhaseProp(ByVal props As Object, ByVal phaseLabel As String) Implements ICapeThermoPropertyRoutine.CalcSinglePhaseProp

            If Not Calculator.CAPEOPENMode Then

                For Each pi As PhaseInfo In Me.PhaseMappings.Values
                    If phaseLabel = pi.PhaseLabel Then
                        For Each p As String In props
                            Me.DW_CalcProp(p, pi.DWPhaseID)
                        Next
                        'Me.DW_CalcPhaseProps(pi.DWPhaseID)
                        Exit For
                    End If
                Next

            Else

                Dim res As New ArrayList
                Dim comps As New ArrayList

                If TryCast(Me, PropertyPackages.CAPEOPENPropertyPackage) IsNot Nothing Then
                    Dim complist As Object = Nothing
                    Me.GetCompoundList(complist, Nothing, Nothing, Nothing, Nothing, Nothing)
                    For Each s As String In complist
                        For Each kvp As KeyValuePair(Of String, String) In CType(Me, PropertyPackages.CAPEOPENPropertyPackage)._mappings
                            If kvp.Value = s Then
                                comps.Add(kvp.Key)
                                Exit For
                            End If
                        Next
                    Next
                Else
                    For Each c As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                        comps.Add(c.Name)
                    Next
                End If

                Dim f As Integer = -1
                Dim phs As PropertyPackages.Phase
                Select Case phaseLabel.ToLower
                    Case "overall"
                        f = 0
                        phs = PropertyPackages.Phase.Mixture
                    Case Else
                        For Each pi As PhaseInfo In Me.PhaseMappings.Values
                            If phaseLabel = pi.PhaseLabel Then
                                f = pi.DWPhaseIndex
                                phs = pi.DWPhaseID
                                Exit For
                            End If
                        Next
                End Select

                If f = -1 Then
                    Dim ex As New CapeOpen.CapeInvalidArgumentException("Invalid Phase ID", New ArgumentException, 0)
                    Dim hcode As Integer = 0
                    ThrowCAPEException(ex, "Error", ex.Message, "ICapeThermoMaterial", ex.Source, ex.StackTrace, "CalcSinglePhaseProp", hcode)
                End If

                Dim basis As String = "Mole"

                For Each [property] As String In props

                    Dim mymo As ICapeThermoMaterial = _como
                    Dim T As Double
                    Dim P As Double
                    Dim Vx As Object = Nothing

                    mymo.GetTPFraction(phaseLabel, T, P, Vx)

                    Me.CurrentMaterialStream.Phases(0).Properties.temperature = T
                    Me.CurrentMaterialStream.Phases(0).Properties.pressure = P
                    Me.CurrentMaterialStream.SetPhaseComposition(Vx, phs)

                    If phs = Phase.Solid Then
                        Me.DW_CalcSolidPhaseProps()
                    Else
                        Me.DW_CalcProp([property], phs)
                    End If

                    basis = "Mole"
                    Select Case [property].ToLower
                        Case "compressibilityfactor"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.compressibilityFactor.GetValueOrDefault)
                            basis = ""
                        Case "heatofvaporization"
                        Case "heatcapacity", "heatcapacitycp"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.heatCapacityCp.GetValueOrDefault * Me.AUX_MMM(phs))
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.heatCapacityCp.GetValueOrDefault * 1000)
                            End Select
                        Case "heatcapacitycv"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.heatCapacityCv.GetValueOrDefault * Me.AUX_MMM(phs))
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.heatCapacityCv.GetValueOrDefault * 1000)
                            End Select
                        Case "idealgasheatcapacity"
                            If f = 1 Then
                                res.Add(Me.AUX_CPm(PropertyPackages.Phase.Liquid, Me.CurrentMaterialStream.Phases(0).Properties.temperature * 1000))
                            ElseIf f = 2 Then
                                res.Add(Me.AUX_CPm(PropertyPackages.Phase.Vapor, Me.CurrentMaterialStream.Phases(0).Properties.temperature * 1000))
                            Else
                                res.Add(Me.AUX_CPm(PropertyPackages.Phase.Solid, Me.CurrentMaterialStream.Phases(0).Properties.temperature * 1000))
                            End If
                        Case "idealgasenthalpy"
                            If f = 1 Then
                                res.Add(Me.RET_Hid(298.15, Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault * 1000, PropertyPackages.Phase.Liquid))
                            ElseIf f = 2 Then
                                res.Add(Me.RET_Hid(298.15, Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault * 1000, PropertyPackages.Phase.Vapor))
                            Else
                                res.Add(Me.RET_Hid(298.15, Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault * 1000, PropertyPackages.Phase.Solid))
                            End If
                        Case "excessenthalpy"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.excessEnthalpy.GetValueOrDefault * Me.AUX_MMM(phs))
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.excessEnthalpy.GetValueOrDefault * 1000)
                            End Select
                        Case "excessentropy"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.excessEntropy.GetValueOrDefault * Me.AUX_MMM(phs))
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.excessEntropy.GetValueOrDefault * 1000)
                            End Select
                        Case "viscosity"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.viscosity.GetValueOrDefault)
                            basis = ""
                        Case "thermalconductivity"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.thermalConductivity.GetValueOrDefault)
                            basis = ""
                        Case "fugacity"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MoleFraction.GetValueOrDefault * Me.CurrentMaterialStream.Phases(f).Compounds(c).FugacityCoeff.GetValueOrDefault * Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault)
                            Next
                            basis = ""
                        Case "activity"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).ActivityCoeff.GetValueOrDefault * Me.CurrentMaterialStream.Phases(f).Compounds(c).MoleFraction.GetValueOrDefault)
                            Next
                            basis = ""
                        Case "fugacitycoefficient"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).FugacityCoeff.GetValueOrDefault)
                            Next
                            basis = ""
                        Case "activitycoefficient"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).ActivityCoeff.GetValueOrDefault)
                            Next
                            basis = ""
                        Case "logfugacitycoefficient"
                            For Each c As String In comps
                                res.Add(Math.Log(Me.CurrentMaterialStream.Phases(f).Compounds(c).FugacityCoeff.GetValueOrDefault))
                            Next
                            basis = ""
                        Case "volume"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault / Me.CurrentMaterialStream.Phases(f).Properties.density.GetValueOrDefault / 1000)
                        Case "density"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.density.GetValueOrDefault / Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault * 1000)
                        Case "enthalpy", "enthalpynf"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    Dim val = Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault
                                    If val = 0.0# Then
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molar_enthalpy.GetValueOrDefault)
                                    Else
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.enthalpy.GetValueOrDefault * val)
                                    End If
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.enthalpy.GetValueOrDefault * 1000)
                            End Select
                        Case "entropy", "entropynf"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    Dim val = Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault
                                    If val = 0.0# Then
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molar_entropy.GetValueOrDefault)
                                    Else
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.entropy.GetValueOrDefault * val)
                                    End If
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.entropy.GetValueOrDefault * 1000)
                            End Select
                        Case "enthalpyf"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    Dim val = Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault
                                    If val = 0.0# Then
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molar_enthalpyF.GetValueOrDefault)
                                    Else
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.enthalpyF.GetValueOrDefault * val)
                                    End If
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.enthalpyF.GetValueOrDefault * 1000)
                            End Select
                        Case "entropyf"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    Dim val = Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault
                                    If val = 0.0# Then
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molar_entropyF.GetValueOrDefault)
                                    Else
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Properties.entropyF.GetValueOrDefault * val)
                                    End If
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.entropy.GetValueOrDefault * 1000)
                            End Select
                        Case "moles"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molarflow.GetValueOrDefault)
                            basis = ""
                        Case "mass"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.massflow.GetValueOrDefault)
                            basis = ""
                        Case "molecularweight"
                            res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molecularWeight.GetValueOrDefault)
                            basis = ""
                        Case "temperature"
                            res.Add(Me.CurrentMaterialStream.Phases(0).Properties.temperature.GetValueOrDefault)
                            basis = ""
                        Case "pressure"
                            res.Add(Me.CurrentMaterialStream.Phases(0).Properties.pressure.GetValueOrDefault)
                            basis = ""
                        Case "flow"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    For Each c As String In comps
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MolarFlow.GetValueOrDefault)
                                    Next
                                Case "Mass", "mass"
                                    For Each c As String In comps
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MassFlow.GetValueOrDefault)
                                    Next
                            End Select
                        Case "fraction", "massfraction", "molarfraction"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    For Each c As String In comps
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MoleFraction.GetValueOrDefault)
                                    Next
                                Case "Mass", "mass"
                                    For Each c As String In comps
                                        res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MassFraction.GetValueOrDefault)
                                    Next
                                Case ""
                                    If [property].ToLower.Contains("mole") Then
                                        For Each c As String In comps
                                            res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MoleFraction.GetValueOrDefault)
                                        Next
                                    ElseIf [property].ToLower.Contains("mass") Then
                                        For Each c As String In comps
                                            res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MassFraction.GetValueOrDefault)
                                        Next
                                    End If
                            End Select
                        Case "concentration"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MassFlow.GetValueOrDefault / Me.CurrentMaterialStream.Phases(f).Properties.volumetric_flow.GetValueOrDefault)
                            Next
                            basis = ""
                        Case "molarity"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(f).Compounds(c).MolarFlow.GetValueOrDefault / Me.CurrentMaterialStream.Phases(f).Properties.volumetric_flow.GetValueOrDefault)
                            Next
                            basis = ""
                        Case "phasefraction"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molarfraction.GetValueOrDefault)
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.massfraction.GetValueOrDefault)
                            End Select
                        Case "totalflow"
                            Select Case basis
                                Case "Molar", "molar", "mole", "Mole"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.molarflow.GetValueOrDefault)
                                Case "Mass", "mass"
                                    res.Add(Me.CurrentMaterialStream.Phases(f).Properties.massflow.GetValueOrDefault)
                            End Select
                        Case Else
                            Dim ex = New CapeOpen.CapeThrmPropertyNotAvailableException
                            Dim hcode As Integer = 0
                            ThrowCAPEException(ex, "Error", ex.Message, "ICapeThermoMaterial", ex.Source, ex.StackTrace, "CalcSinglePhaseProp", hcode)
                    End Select

                    Dim i As Integer
                    For i = 0 To res.Count - 1
                        If Double.IsNaN(res(i)) Then res(i) = 0.0#
                    Next

                    Dim arr(res.Count - 1) As Double
                    Array.Copy(res.ToArray, arr, res.Count)

                    mymo.SetSinglePhaseProp([property], phaseLabel, basis, arr)

                Next

            End If

        End Sub

        ''' <summary>
        ''' CalcTwoPhaseProp is used to calculate mixture properties and property derivatives that depend on
        ''' two Phases at the current values of temperature, pressure and composition set in the Material Object.
        ''' It does not perform Equilibrium Calculations.
        ''' </summary>
        ''' <param name="props">The list of identifiers for properties to be calculated. This must be one or more 
        ''' of the supported two-phase properties and derivatives (as given by the GetTwoPhasePropList method). 
        ''' The standard identifiers for two-phase properties are given in section 7.5.6 and 7.6.</param>
        ''' <param name="phaseLabels">Phase labels of the phases for which the properties are to be calculated. 
        ''' The phase labels must be two of the strings returned by the GetPhaseList method on the ICapeThermoPhases 
        ''' interface and the phases must also be present in the Material Object.</param>
        ''' <remarks>CalcTwoPhaseProp calculates the values of properties such as surface tension or K-values.
        ''' Properties that pertain to a single Phase are handled by the CalcSinglePhaseProp method of
        ''' the ICapeThermoPropertyRoutine interface.Components that implement this method must
        ''' get the input specification for the calculation (temperature, pressure and composition) from
        ''' the associated Material Object and set the results in the Material Object.
        ''' Components such as a Property Package or Property Calculator must implement the
        ''' ICapeThermoMaterialContext interface so that an ICapeThermoMaterial interface can be
        ''' passed via the SetMaterial method.
        ''' The component that implements the ICapeThermoPropertyRoutine interface (e.g. a Property
        ''' Package or Property Calculator) must also implement the ICapeThermoPhases interface so
        ''' that it is possible to get a list of supported phases. The phaseLabels passed to this method
        ''' must be in the list of phase labels returned by the GetPhaseList method of the
        ''' ICapeThermoPhases interface and they must also be present in the Material Object, ie. in the
        ''' list of phase labels returned by the GetPresentPhases method of the ICapeThermoMaterial
        ''' interface. This latter condition will be satisfied if the phases are made present explicitly by
        ''' calling the SetPresentPhases method or if any phase properties have been set by calling the
        ''' SetSinglePhaseProp or SetTwoPhaseProp methods.</remarks>
        Public Overridable Sub CalcTwoPhaseProp(ByVal props As Object, ByVal phaseLabels As Object) Implements ICapeThermoPropertyRoutine.CalcTwoPhaseProp

            Me.DW_CalcTwoPhaseProps(Phase.Liquid, Phase.Vapor)

            If Calculator.CAPEOPENMode Then

                Dim res As New ArrayList
                Dim comps As New ArrayList
                For Each c As Interfaces.ICompound In Me.CurrentMaterialStream.Phases(0).Compounds.Values
                    comps.Add(c.Name)
                Next

                Dim basis As String = Nothing

                For Each [property] As String In props
                    Select Case [property].ToLower
                        Case "kvalue"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(0).Compounds(c).Kvalue)
                            Next
                        Case "logkvalue"
                            For Each c As String In comps
                                res.Add(Me.CurrentMaterialStream.Phases(0).Compounds(c).lnKvalue)
                            Next
                        Case "surfacetension"
                            res.Add(Me.CurrentMaterialStream.Phases(0).Properties.surfaceTension.GetValueOrDefault)
                        Case Else
                            Dim ex = New CapeOpen.CapeThrmPropertyNotAvailableException
                            Dim hcode As Integer = 0
                            ThrowCAPEException(ex, "Error", ex.Message, "ICapeThermoMaterial", ex.Source, ex.StackTrace, "CalcTwoPhaseProp", hcode)
                    End Select

                    Dim arr(res.Count - 1) As Double
                    Array.Copy(res.ToArray, arr, res.Count)

                    Dim mymo As ICapeThermoMaterial = _como
                    mymo.SetTwoPhaseProp([property], phaseLabels, basis, arr)

                Next

            End If

        End Sub

        ''' <summary>
        ''' Checks whether it is possible to calculate a property with the CalcSinglePhaseProp method for a given Phase.
        ''' </summary>
        ''' <param name="property">The identifier of the property to check. To be valid this must be one of the supported 
        ''' single-phase properties or derivatives (as given by the GetSinglePhasePropList method).</param>
        ''' <param name="phaseLabel">The Phase label for the calculation check. This must be one of the labels 
        ''' returned by the GetPhaseList method on the ICapeThermoPhases interface.</param>
        ''' <returns>Set to True if the combination of property and phaseLabel is supported or False if 
        ''' not supported.</returns>
        ''' <remarks>The result of the check should only depend on the capabilities and configuration
        ''' (Compounds and Phases supported) of the component that implements the
        ''' ICapeThermoPropertyRoutine interface (e.g. a Property Package). It should not depend on
        ''' whether a Material Object has been set nor on the state (temperature, pressure, composition
        ''' etc.), or configuration of a Material Object that might be set.
        ''' It is expected that the PME, or other client, will use this method to check whether the properties
        ''' it requires are supported by the Property Package when the package is imported. If any
        ''' essential properties are not available, the import process should be aborted.
        ''' If either the property or the phaseLabel arguments are not recognised by the component that
        ''' implements the ICapeThermoPropertyRoutine interface this method should return False.</remarks>
        Public Overridable Function CheckSinglePhasePropSpec(ByVal [property] As String, ByVal phaseLabel As String) As Boolean Implements ICapeThermoPropertyRoutine.CheckSinglePhasePropSpec
            Select Case [property].ToLower
                Case "compressibilityfactor", "heatofvaporization", "heatcapacity", "heatcapacitycv", _
                    "idealgasheatcapacity", "idealgasenthalpy", "excessenthalpy", "excessentropy", _
                    "viscosity", "thermalconductivity", "fugacity", "fugacitycoefficient", "activity", "activitycoefficient", _
                    "dewpointpressure", "dewpointtemperature", "logfugacitycoefficient", "volume", "density", _
                    "enthalpy", "entropy", "gibbsfreeenergy", "moles", "mass", "molecularweight", "totalflow"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        ''' <summary>
        ''' Checks whether it is possible to calculate a property with the CalcTwoPhaseProp method for a given set of Phases.
        ''' </summary>
        ''' <param name="property">The identifier of the property to check. To be valid this must be one of the supported 
        ''' two-phase properties (including derivatives), as given by the GetTwoPhasePropList method.</param>
        ''' <param name="phaseLabels">Phase labels of the Phases for which the properties are to be calculated. The Phase 
        ''' labels must be two of the identifiers returned by the GetPhaseList method on the ICapeThermoPhases interface.</param>
        ''' <returns>Set to True if the combination of property and phaseLabels is supported, or False if not supported.</returns>
        ''' <remarks>The result of the check should only depend on the capabilities and configuration
        ''' (Compounds and Phases supported) of the component that implements the
        ''' ICapeThermoPropertyRoutine interface (e.g. a Property Package). It should not depend on
        ''' whether a Material Object has been set nor on the state (temperature, pressure, composition
        ''' etc.), or configuration of a Material Object that might be set.
        ''' It is expected that the PME, or other client, will use this method to check whether the
        ''' properties it requires are supported by the Property Package when the Property Package is
        ''' imported. If any essential properties are not available, the import process should be aborted.
        ''' If either the property argument or the values in the phaseLabels arguments are not
        ''' recognised by the component that implements the ICapeThermoPropertyRoutine interface
        ''' this method should return False.</remarks>
        Public Overridable Function CheckTwoPhasePropSpec(ByVal [property] As String, ByVal phaseLabels As Object) As Boolean Implements ICapeThermoPropertyRoutine.CheckTwoPhasePropSpec
            Return True
        End Function

        ''' <summary>
        ''' Returns the list of supported non-constant single-phase Physical Properties.
        ''' </summary>
        ''' <returns>List of all supported non-constant single-phase property identifiers. 
        ''' The standard single-phase property identifiers are listed in section 7.5.5.</returns>
        ''' <remarks>A non-constant property depends on the state of the Material Object.
        ''' Single-phase properties, e.g. enthalpy, only depend on the state of one phase.
        ''' GetSinglePhasePropList must return all the single-phase properties that can be calculated by
        ''' CalcSinglePhaseProp. If derivatives can be calculated these must also be returned. The list
        ''' of standard property identifiers in section 7.5.5 also contains properties such as temperature,
        ''' pressure, fraction, phaseFraction, flow and totalFlow that are not usually calculated by the
        ''' CalcSinglePhaseProp method and hence these property identifiers would not be returned by
        ''' GetSinglePhasePropList. These properties would normally be used in calls to the
        ''' Set/GetSinglePhaseProp methods of the ICapeThermoMaterial interface.
        ''' If no single-phase properties are supported this method should return UNDEFINED.
        ''' To get the list of supported two-phase properties, use GetTwoPhasePropList.
        ''' A component that implements this method may return non-constant single-phase property
        ''' identifiers which do not belong to the list defined in section 7.5.5. However, these
        ''' proprietary identifiers may not be understood by most of the clients of this component.</remarks>
        Public Overridable Function GetSinglePhasePropList() As Object Implements ICapeThermoPropertyRoutine.GetSinglePhasePropList
            Dim arr As New ArrayList
            With arr
                .Add("compressibilityFactor")
                .Add("heatCapacityCp")
                .Add("heatCapacityCv")
                .Add("excessEnthalpy")
                .Add("excessEntropy")
                .Add("viscosity")
                .Add("thermalConductivity")
                .Add("fugacity")
                .Add("fugacityCoefficient")
                .Add("activity")
                .Add("activityCoefficient")
                .Add("logFugacityCoefficient")
                .Add("volume")
                .Add("density")
                .Add("enthalpy")
                .Add("entropy")
                .Add("enthalpyF")
                .Add("entropyF")
                .Add("enthalpyNF")
                .Add("entropyNF")
                .Add("molecularWeight")
            End With
            Dim arr2(arr.Count - 1) As String
            Array.Copy(arr.ToArray, arr2, arr.Count)
            Return arr2
        End Function

        ''' <summary>
        ''' Returns the list of supported non-constant two-phase properties.
        ''' </summary>
        ''' <returns>List of all supported non-constant two-phase property identifiers. The standard two-phase 
        ''' property identifiers are listed in section 7.5.6.</returns>
        ''' <remarks>A non-constant property depends on the state of the Material Object. Two-phase properties
        ''' are those that depend on more than one co-existing phase, e.g. K-values.
        ''' GetTwoPhasePropList must return all the properties that can be calculated by
        ''' CalcTwoPhaseProp. If derivatives can be calculated, these must also be returned.
        ''' If no two-phase properties are supported this method should return UNDEFINED.
        ''' To check whether a property can be evaluated for a particular set of phase labels use the
        ''' CheckTwoPhasePropSpec method.
        ''' A component that implements this method may return non-constant two-phase property
        ''' identifiers which do not belong to the list defined in section 7.5.6. However, these
        ''' proprietary identifiers may not be understood by most of the clients of this component.
        ''' To get the list of supported single-phase properties, use GetSinglePhasePropList.</remarks>
        Public Overridable Function GetTwoPhasePropList() As Object Implements ICapeThermoPropertyRoutine.GetTwoPhasePropList
            Return New String() {"kvalue", "logKvalue", "surfaceTension"}
        End Function

        ''' <summary>
        ''' Retrieves the value of a Universal Constant.
        ''' </summary>
        ''' <param name="constantId">Identifier of Universal Constant. The list of constants supported should be 
        ''' obtained by using the GetUniversalConstantList method.</param>
        ''' <returns>Value of Universal Constant. This could be a numeric or a string value. For numeric values 
        ''' the units of measurement are specified in section 7.5.1.</returns>
        ''' <remarks>Universal Constants (often called fundamental constants) are quantities like the gas constant,
        ''' or the Avogadro constant.</remarks>
        Public Overridable Function GetUniversalConstant1(ByVal constantId As String) As Object Implements ICapeThermoUniversalConstant.GetUniversalConstant
            Dim res As New ArrayList
            Select Case constantId.ToLower
                Case "standardaccelerationofgravity"
                    res.Add(9.80665)
                Case "avogadroconstant"
                    res.Add(6.0221419947E+23)
                Case "boltzmannconstant"
                    res.Add(1.38065324E-23)
                Case "molargasconstant"
                    res.Add(8.31447215)
            End Select
            Dim arr2(res.Count) As Object
            Array.Copy(res.ToArray, arr2, res.Count)
            Return arr2
        End Function

        ''' <summary>
        ''' Returns the identifiers of the supported Universal Constants.
        ''' </summary>
        ''' <returns>List of identifiers of Universal Constants. The list of standard identifiers is given in section 7.5.1.</returns>
        ''' <remarks>A component may return Universal Constant identifiers that do not belong to the list defined
        ''' in section 7.5.1. However, these proprietary identifiers may not be understood by most of the
        ''' clients of this component.</remarks>
        Public Overridable Function GetUniversalConstantList() As Object Implements ICapeThermoUniversalConstant.GetUniversalConstantList
            Return New String() {"standardAccelerationOfGravity", "avogadroConstant", "boltzmannConstant", "molarGasConstant"}
        End Function

        ''' <summary>
        ''' CalcEquilibrium is used to calculate the amounts and compositions of Phases at equilibrium.
        ''' CalcEquilibrium will calculate temperature and/or pressure if these are not among the two
        ''' specifications that are mandatory for each Equilibrium Calculation considered.
        ''' </summary>
        ''' <param name="specification1">First specification for the Equilibrium Calculation. The 
        ''' specification information is used to retrieve the value of the specification from the 
        ''' Material Object. See below for details.</param>
        ''' <param name="specification2">Second specification for the Equilibrium Calculation in 
        ''' the same format as specification1.</param>
        ''' <param name="solutionType">The identifier for the required solution type. The
        ''' standard identifiers are given in the following list:
        ''' Unspecified
        ''' Normal
        ''' Retrograde
        ''' The meaning of these terms is defined below in the notes. Other identifiers may be supported 
        ''' but their interpretation is not part of the CO standard.</param>
        ''' <remarks>The specification1 and specification2 arguments provide the information necessary to
        ''' retrieve the values of two specifications, for example the pressure and temperature, for the
        ''' Equilibrium Calculation. The CheckEquilibriumSpec method can be used to check for
        ''' supported specifications. Each specification variable contains a sequence of strings in the
        ''' order defined in the following table (hence, the specification arguments may have 3 or 4
        ''' items):
        ''' 
        ''' item                        meaning
        ''' 
        ''' property identifier         The property identifier can be any of the identifiers listed in section 7.5.5 but
        '''                             only certain property specifications will normally be supported by any
        '''                             Equilibrium Routine.
        ''' 
        ''' basis                       The basis for the property value. Valid settings for basis are given in section
        '''                             7.4. Use UNDEFINED as a placeholder for a property for which basis does
        '''                             not apply. For most Equilibrium Specifications, the result of the calculation
        '''                             is not dependent on the basis, but, for example, for phase fraction
        '''                             specifications the basis (Mole or Mass) does make a difference.
        ''' 
        ''' phase label                 The phase label denotes the Phase to which the specification applies. It must
        '''                             either be one of the labels returned by GetPresentPhases, or the special value
        '''                             “Overall”.
        ''' 
        ''' compound identifier         The compound identifier allows for specifications that depend on a particular
        '''                             Compound. This item of the specification array is optional and may be
        '''                             omitted. In case of a specification without compound identifier, the array
        '''                             element may be present and empty, or may be absent.
        '''                             The values corresponding to the specifications in the argument list and the overall
        '''                             composition of the mixture must be set in the associated Material Object before a call to
        '''                             CalcEquilibrium.
        ''' 
        ''' Components such as a Property Package or an Equilibrium Calculator must implement the
        ''' ICapeThermoMaterialContext interface, so that an ICapeThermoMaterial interface can be
        ''' passed via the SetMaterial method. It is the responsibility of the implementation of
        ''' CalcEquilibrium to validate the Material Object before attempting a calculation.
        ''' The Phases that will be considered in the Equilibrium Calculation are those that exist in the
        ''' Material Object, i.e. the list of phases specified in a SetPresentPhases call. This provides a
        ''' way for a client to specify whether, for example, a vapour-liquid, liquid-liquid, or vapourliquid-
        ''' liquid calculation is required. CalcEquilibrium must use the GetPresentPhases method
        ''' to retrieve the list of Phases and the associated Phase status flags. The Phase status flags may
        ''' be used by the client to provide information about the Phases, for example whether estimates
        ''' of the equilibrium state are provided. See the description of the GetPresentPhases and
        ''' SetPresentPhases methods of the ICapeThermoMaterial interface for details. When the
        ''' Equilibrium Calculation has been completed successfully, the SetPresentPhases method
        ''' must be used to specify which Phases are present at equilibrium and the Phase status flags
        ''' for the phases should be set to Cape_AtEquilibrium. This must include any Phases that are
        ''' present in zero amount such as the liquid Phase in a dew point calculation.
        ''' Some types of Phase equilibrium specifications may result in more than one solution. A
        ''' common example of this is the case of a dew point calculation. However, CalcEquilibrium
        ''' can provide only one solution through the Material Object. The solutionType argument
        ''' allows the “Normal” or “Retrograde” solution to be explicitly requested. When none of the
        ''' specifications includes a phase fraction, the solutionType argument should be set to
        ''' “Unspecified”.
        ''' 
        ''' CalcEquilibrium must set the amounts (phase fractions), compositions, temperature and
        ''' pressure for all Phases present at equilibrium, as well as the temperature and pressure for the
        ''' overall mixture if not set as part of the calculation specifications. It must not set any other
        ''' values – in particular it must not set any values for phases that are not present.
        ''' 
        ''' As an example, the following sequence of operations might be performed by
        ''' CalcEquilibrium in the case of an Equilibrium Calculation at fixed pressure and temperature:
        ''' 
        ''' - With the ICapeThermoMaterial interface of the supplied Material Object:
        ''' 
        ''' -- Use the GetPresentPhases method to find the list of Phases that the Equilibrium
        ''' Calculation should consider.
        ''' 
        ''' -- With the ICapeThermoCompounds interface of the Material Object use the
        ''' GetCompoundList method to find which Compounds are present.
        ''' 
        ''' -- Use the GetOverallProp method to get the temperature, pressure and composition
        ''' for the overall mixture.
        ''' 
        ''' - Perform the Equilibrium Calculation.
        ''' 
        ''' -- Use SetPresentPhases to specify the Phases present at equilibrium and set the
        ''' Phase status flags to Cape_AtEquilibrium.
        ''' 
        ''' -- Use SetSinglePhaseProp to set pressure, temperature, Phase amount (or Phase
        ''' fraction) and composition for all Phases present.</remarks>
        Public Overridable Sub CalcEquilibrium1(ByVal specification1 As Object, ByVal specification2 As Object, ByVal solutionType As String) Implements ICapeThermoEquilibriumRoutine.CalcEquilibrium

            Dim spec1, spec2 As FlashSpec
            If specification1(0).ToString.ToLower = "temperature" And specification2(0).ToString.ToLower = "pressure" Then
                spec1 = FlashSpec.T
                spec2 = FlashSpec.P
            ElseIf specification1(0).ToString.ToLower = "pressure" And specification2(0).ToString.ToLower = "enthalpy" Then
                spec1 = FlashSpec.P
                spec2 = FlashSpec.H
            ElseIf specification1(0).ToString.ToLower = "pressure" And specification2(0).ToString.ToLower = "entropy" Then
                spec1 = FlashSpec.P
                spec2 = FlashSpec.S
            ElseIf specification1(0).ToString.ToLower = "pressure" And specification2(0).ToString.ToLower = "phasefraction" Then
                spec1 = FlashSpec.P
                spec2 = FlashSpec.VAP
            ElseIf specification1(0).ToString.ToLower = "temperature" And specification2(0).ToString.ToLower = "phasefraction" Then
                spec1 = FlashSpec.T
                spec2 = FlashSpec.VAP
            ElseIf specification2(0).ToString.ToLower = "temperature" And specification1(0).ToString.ToLower = "pressure" Then
                spec1 = FlashSpec.T
                spec2 = FlashSpec.P
            ElseIf specification2(0).ToString.ToLower = "pressure" And specification1(0).ToString.ToLower = "enthalpy" Then
                spec1 = FlashSpec.P
                spec2 = FlashSpec.H
            ElseIf specification2(0).ToString.ToLower = "pressure" And specification1(0).ToString.ToLower = "entropy" Then
                spec1 = FlashSpec.P
                spec2 = FlashSpec.S
            ElseIf specification2(0).ToString.ToLower = "pressure" And specification1(0).ToString.ToLower = "phasefraction" Then
                spec1 = FlashSpec.P
                spec2 = FlashSpec.VAP
            ElseIf specification2(0).ToString.ToLower = "temperature" And specification1(0).ToString.ToLower = "phasefraction" Then
                spec1 = FlashSpec.T
                spec2 = FlashSpec.VAP
            Else
                Throw New CapeOpen.CapeLimitedImplException("Flash spec not supported.")
            End If

            Dim T, P, Hm, Sm As Double

            If Calculator.CAPEOPENMode Then

                Dim res As Object = Nothing

                Dim mys As ICapeThermoMaterial = _como

                If spec1 = FlashSpec.T And spec2 = P Then
                    mys.GetOverallProp("temperature", Nothing, res)
                    T = res(0)
                    mys.GetOverallProp("pressure", Nothing, res)
                    P = res(0)
                    Me.CurrentMaterialStream.Phases(0).Properties.temperature = T
                    Me.CurrentMaterialStream.Phases(0).Properties.pressure = P
                ElseIf spec1 = FlashSpec.T And spec2 = FlashSpec.VAP Then
                    mys.GetOverallProp("temperature", Nothing, res)
                    T = res(0)
                    Me.CurrentMaterialStream.Phases(0).Properties.temperature = T
                    mys.GetSinglePhaseProp("phaseFraction", "Vapor", "Mole", res)
                    Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = res(0)
                ElseIf spec1 = FlashSpec.P And spec2 = FlashSpec.H Then
                    mys.GetOverallProp("pressure", Nothing, res)
                    P = res(0)
                    mys.GetOverallProp("enthalpy", "Mole", res)
                    Hm = res(0) / AUX_MMM(Phase.Mixture)
                    Me.CurrentMaterialStream.Phases(0).Properties.enthalpy = Hm
                    Me.CurrentMaterialStream.Phases(0).Properties.pressure = P
                ElseIf spec1 = FlashSpec.P And spec2 = FlashSpec.S Then
                    mys.GetOverallProp("pressure", Nothing, res)
                    P = res(0)
                    mys.GetOverallProp("entropy", "Mole", res)
                    Sm = res(0) / AUX_MMM(Phase.Mixture)
                    Me.CurrentMaterialStream.Phases(0).Properties.entropy = Sm
                    Me.CurrentMaterialStream.Phases(0).Properties.pressure = P
                ElseIf spec1 = FlashSpec.P And spec2 = FlashSpec.VAP Then
                    mys.GetOverallProp("pressure", Nothing, res)
                    P = res(0)
                    Me.CurrentMaterialStream.Phases(0).Properties.pressure = P
                    mys.GetSinglePhaseProp("phaseFraction", "Vapor", "Mole", res)
                    Me.CurrentMaterialStream.Phases(2).Properties.molarfraction = res(0)
                End If

            End If

            Try
                Me.DW_CalcEquilibrium(spec1, spec2)
            Catch ex As Exception
                ThrowCAPEException(ex, ex.GetType.ToString, ex.ToString, "ICapeThermoEquilibriumRoutine", ex.ToString, "CalcEquilibrium", "", 0)
            End Try

            If Calculator.CAPEOPENMode Then

                Dim ms As Interfaces.IMaterialStream = Me.CurrentMaterialStream
                Dim mo As ICapeThermoMaterial = _como

                Dim vok As Boolean = False
                Dim l1ok As Boolean = False
                Dim l2ok As Boolean = False
                Dim sok As Boolean = False

                If ms.Phases(2).Properties.molarfraction.HasValue Then vok = True
                If ms.Phases(3).Properties.molarfraction.HasValue Then l1ok = True
                If ms.Phases(4).Properties.molarfraction.HasValue Then l2ok = True
                If ms.Phases(7).Properties.molarfraction.HasValue Then sok = True

                Dim phases As String() = Nothing
                Dim statuses As CapePhaseStatus() = Nothing

                If vok And l1ok And l2ok Then
                    phases = New String() {"Vapor", "Liquid", "Liquid2"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf vok And l1ok And Not l2ok Then
                    phases = New String() {"Vapor", "Liquid"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf vok And l1ok And Not l2ok And sok Then
                    phases = New String() {"Vapor", "Liquid", "Solid"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf vok And Not l1ok And l2ok Then
                    phases = New String() {"Vapor", "Liquid2"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf Not vok And l1ok And l2ok Then
                    phases = New String() {"Liquid", "Liquid2"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf vok And Not l1ok And Not l2ok Then
                    phases = New String() {"Vapor"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf Not vok And l1ok And Not l2ok Then
                    phases = New String() {"Liquid"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf vok And Not l1ok And sok Then
                    phases = New String() {"Vapor", "Solid"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf Not vok And l1ok And sok Then
                    phases = New String() {"Liquid", "Solid"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM, CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf Not vok And Not l1ok And l2ok Then
                    phases = New String() {"Liquid2"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                ElseIf Not vok And Not l1ok And sok Then
                    phases = New String() {"Solid"}
                    statuses = New CapePhaseStatus() {CapePhaseStatus.CAPE_ATEQUILIBRIUM}
                End If

                mo.SetPresentPhases(phases, statuses)

                Dim nc As Integer = ms.Phases(0).Compounds.Count
                T = ms.Phases(0).Properties.temperature.GetValueOrDefault
                P = ms.Phases(0).Properties.pressure.GetValueOrDefault

                Dim vz(nc - 1), pf As Double, i As Integer

                mo.SetOverallProp("temperature", Nothing, New Double() {T})
                mo.SetOverallProp("pressure", Nothing, New Double() {P})

                If vok Then

                    i = 0
                    For Each s As Interfaces.ICompound In ms.Phases(2).Compounds.Values
                        vz(i) = s.MoleFraction.GetValueOrDefault
                        i += 1
                    Next
                    pf = ms.Phases(2).Properties.molarfraction.GetValueOrDefault

                    mo.SetSinglePhaseProp("temperature", "Vapor", Nothing, New Double() {T})
                    mo.SetSinglePhaseProp("pressure", "Vapor", Nothing, New Double() {P})
                    mo.SetSinglePhaseProp("phasefraction", "Vapor", "Mole", New Double() {pf})
                    mo.SetSinglePhaseProp("fraction", "Vapor", "Mole", vz)

                End If

                If l1ok Then

                    i = 0
                    For Each s As Interfaces.ICompound In ms.Phases(3).Compounds.Values
                        vz(i) = s.MoleFraction.GetValueOrDefault
                        i += 1
                    Next
                    pf = ms.Phases(3).Properties.molarfraction.GetValueOrDefault

                    mo.SetSinglePhaseProp("temperature", "Liquid", Nothing, New Double() {T})
                    mo.SetSinglePhaseProp("pressure", "Liquid", Nothing, New Double() {P})
                    mo.SetSinglePhaseProp("phasefraction", "Liquid", "Mole", New Double() {pf})
                    mo.SetSinglePhaseProp("fraction", "Liquid", "Mole", vz)

                End If

                If l2ok Then

                    i = 0
                    For Each s As Interfaces.ICompound In ms.Phases(4).Compounds.Values
                        vz(i) = s.MoleFraction.GetValueOrDefault
                        i += 1
                    Next
                    pf = ms.Phases(4).Properties.molarfraction.GetValueOrDefault

                    mo.SetSinglePhaseProp("temperature", "Liquid2", Nothing, New Double() {T})
                    mo.SetSinglePhaseProp("pressure", "Liquid2", Nothing, New Double() {P})
                    mo.SetSinglePhaseProp("phasefraction", "Liquid2", "Mole", New Double() {pf})
                    mo.SetSinglePhaseProp("fraction", "Liquid2", "Mole", vz)

                End If

                If sok Then

                    i = 0
                    For Each s As Interfaces.ICompound In ms.Phases(7).Compounds.Values
                        vz(i) = s.MoleFraction.GetValueOrDefault
                        i += 1
                    Next
                    pf = ms.Phases(7).Properties.molarfraction.GetValueOrDefault

                    mo.SetSinglePhaseProp("temperature", "Solid", Nothing, New Double() {T})
                    mo.SetSinglePhaseProp("pressure", "Solid", Nothing, New Double() {P})
                    mo.SetSinglePhaseProp("phasefraction", "Solid", "Mole", New Double() {pf})
                    mo.SetSinglePhaseProp("fraction", "Solid", "Mole", vz)

                End If

            End If

        End Sub

        ''' <summary>
        ''' Checks whether the Property Package can support a particular type of Equilibrium Calculation.
        ''' </summary>
        ''' <param name="specification1">First specification for the Equilibrium Calculation.</param>
        ''' <param name="specification2">Second specification for the Equilibrium Calculation.</param>
        ''' <param name="solutionType">The required solution type.</param>
        ''' <returns>Set to True if the combination of specifications and solutionType is supported 
        ''' for a particular combination of present phases or False if not supported.</returns>
        ''' <remarks>The meaning of the specification1, specification2 and solutionType arguments is the same as
        ''' for the CalcEquilibrium method. If solutionType, specification1 and specification2
        ''' arguments appear valid but the actual specifications are not supported or not recognised a
        ''' False value should be returned.
        ''' The result of the check should depend primarily on the capabilities and configuration
        ''' (compounds and phases supported) of the component that implements the ICapeThermo-
        ''' EquilibriumRoutine interface (egg. a Property package). A component that supports
        ''' calculation specifications for any combination of supported phases is capable of checking
        ''' the specification without any reference to a Material Object. However, it is possible that
        ''' there may be restrictions on the combinations of phases supported in an equilibrium
        ''' calculation. For example a component may support vapor-liquid and liquid-liquid
        ''' calculations but not vapor-liquid-liquid calculations. In general it is therefore a necessary
        ''' prerequisite that a Material Object has been set (using the SetMaterial method of the
        ''' ICapeThermoMaterialContext interface) and that the SetPresentPhases method of the
        ''' ICapeThermoMaterial interface has been called to specify the combination of phases for the
        ''' equilibrium calculation. The result of the check should not depend on the state (temperature,
        ''' pressure, composition etc.) of the Material Object.</remarks>
        Public Overridable Function CheckEquilibriumSpec(ByVal specification1 As Object, ByVal specification2 As Object, ByVal solutionType As String) As Boolean Implements ICapeThermoEquilibriumRoutine.CheckEquilibriumSpec
            If specification1(0).ToString.ToLower = "temperature" And specification2(0).ToString.ToLower = "pressure" Then
                Return True
            ElseIf specification1(0).ToString.ToLower = "pressure" And specification2(0).ToString.ToLower = "enthalpy" Then
                Return True
            ElseIf specification1(0).ToString.ToLower = "pressure" And specification2(0).ToString.ToLower = "entropy" Then
                Return True
            ElseIf specification1(0).ToString.ToLower = "pressure" And specification2(0).ToString.ToLower = "phasefraction" Then
                Return True
            ElseIf specification1(0).ToString.ToLower = "temperature" And specification2(0).ToString.ToLower = "phasefraction" Then
                Return True
            ElseIf specification2(0).ToString.ToLower = "temperature" And specification1(0).ToString.ToLower = "pressure" Then
                Return True
            ElseIf specification2(0).ToString.ToLower = "pressure" And specification1(0).ToString.ToLower = "enthalpy" Then
                Return True
            ElseIf specification2(0).ToString.ToLower = "pressure" And specification1(0).ToString.ToLower = "entropy" Then
                Return True
            ElseIf specification2(0).ToString.ToLower = "pressure" And specification1(0).ToString.ToLower = "phasefraction" Then
                Return True
            ElseIf specification2(0).ToString.ToLower = "temperature" And specification1(0).ToString.ToLower = "phasefraction" Then
                Return True
            Else
                Return False
            End If
        End Function

        ''' <summary>
        ''' Allows the client of a component that implements this interface to pass an ICapeThermoMaterial 
        ''' interface to the component, so that it can access the properties of a Material.
        ''' </summary>
        ''' <param name="material">The Material interface.</param>
        ''' <remarks>The SetMaterial method allows a Thermodynamic and Physical Properties component, such
        ''' as a Property Package, to be given the ICapeThermoMaterial interface of a Material Object.
        ''' This interface gives the component access to the description of the Material for which
        ''' Property Calculations or Equilibrium Calculations are required. The component can access
        ''' property values directly using this interface. A client can also use the ICapeThermoMaterial
        ''' interface to query a Material Object for its ICapeThermoCompounds and ICapeThermo-
        ''' Phases interfaces, which provide access to Compound and Phase information, respectively.
        ''' It is envisaged that the SetMaterial method will be used to check that the Material Interface
        ''' supplied is valid and useable. For example, a Property Package may check that there are
        ''' some Compounds in a Material Object and that those Compounds can be identified by the
        ''' Property Package. In addition a Property Package may perform any initialisation that
        ''' depends on the configuration of a Material Object. A Property Calculator component might
        ''' typically use this method to query the Material Object for any required information
        ''' concerning the Compounds.
        ''' Calling the UnsetMaterial method of the ICapeThermoMaterialContext interface has the
        ''' effect of removing the interface set by the SetMaterial method.
        ''' After a call to SetMaterial() has been received, the object implementing the ICapeThermo-
        ''' MaterialContext interface can assume that the number, name and order of compounds for
        ''' that Material Object will remain fixed until the next call to SetMaterial() or UnsetMaterial().</remarks>
        Public Overridable Sub SetMaterial(ByVal material As Object) Implements ICapeThermoMaterialContext.SetMaterial
            Me.CurrentMaterialStream = Nothing
            If _como IsNot Nothing Then
                If System.Runtime.InteropServices.Marshal.IsComObject(_como) Then
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(_como)
                End If
            End If
            If Calculator.CAPEOPENMode Then
                _como = material
                If TryCast(material, Interfaces.IMaterialStream) Is Nothing Then
                    Me.CurrentMaterialStream = COMaterialtoDWMaterial(material)
                Else
                    Me.CurrentMaterialStream = material
                End If
            Else
                Me.CurrentMaterialStream = material
            End If
        End Sub

        ''' <summary>
        ''' Removes any previously set Material interface.
        ''' </summary>
        ''' <remarks>The UnsetMaterial method removes any Material interface previously set by a call to the
        ''' SetMaterial method of the ICapeThermoMaterialContext interface. This means that any
        ''' methods of other interfaces that depend on having a valid Material Interface, for example
        ''' methods of the ICapeThermoPropertyRoutine or ICapeThermoEquilibriumRoutine
        ''' interfaces, should behave in the same way as if the SetMaterial method had never been
        ''' called.
        ''' If UnsetMaterial is called before a call to SetMaterial it has no effect and no exception
        ''' should be raised.</remarks>
        Public Overridable Sub UnsetMaterial() Implements ICapeThermoMaterialContext.UnsetMaterial
            Me.CurrentMaterialStream = Nothing
            If Calculator.CAPEOPENMode Then
                If _como IsNot Nothing Then
                    If System.Runtime.InteropServices.Marshal.IsComObject(_como) Then
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(_como)
                    End If
                End If
            End If
        End Sub

        ''' <summary>
        ''' Converts a COM Material Object into a DWSIM Material Stream.
        ''' </summary>
        ''' <param name="material">The Material Object to convert from</param>
        ''' <returns>A DWSIM Material Stream</returns>
        ''' <remarks>This function is called by SetMaterial when DWSIM Property Packages are working in outside environments (CAPE-OPEN COSEs) like COCO/COFE.</remarks>
        Public Function COMaterialtoDWMaterial(ByVal material As Object) As Interfaces.IMaterialStream

            Dim ms As Interfaces.IMaterialStream = DirectCast(Me.CurrentMaterialStream.Flowsheet, CapeOpen.ICapeMaterialTemplateSystem).CreateMaterialTemplate(CType(material, ICapeIdentification).ComponentName)

            'transfer values

            Dim mys As ICapeThermoMaterial = material

            Dim Tv, Tl1, Tl2, Ts, Pv, Pl1, Pl2, Ps, xv, xl1, xl2, xs As Double
            Dim Vz As Object = Nothing
            Dim Vy As Object = Nothing
            Dim Vx1 As Object = Nothing
            Dim Vx2 As Object = Nothing
            Dim Vs As Object = Nothing
            Dim Vwy As Object = Nothing
            Dim Vwx1 As Object = Nothing
            Dim Vwx2 As Object = Nothing
            Dim Vws As Object = Nothing
            Dim labels As Object = Nothing
            Dim statuses As Object = Nothing
            Dim res As Object = Nothing

            Try
                mys.GetOverallProp("fraction", "Mole", res)
                Vz = res
            Catch ex As Exception
                Vz = RET_NullVector()
            End Try

            mys.GetPresentPhases(labels, statuses)

            Dim data(0) As Double

            Dim i As Integer = 0
            Dim n As Integer = UBound(labels)

            For i = 0 To n
                If statuses(i) = CapeOpen.CapePhaseStatus.CAPE_ATEQUILIBRIUM Then
                    Select Case labels(i)
                        Case "Vapor"
                            mys.GetTPFraction(labels(i), Tv, Pv, Vy)
                        Case "Liquid"
                            mys.GetTPFraction(labels(i), Tl1, Pl1, Vx1)
                        Case "Liquid2"
                            mys.GetTPFraction(labels(i), Tl2, Pl2, Vx2)
                        Case "Solid"
                            mys.GetTPFraction(labels(i), Ts, Ps, Vs)
                    End Select
                    Select Case labels(i)
                        Case "Vapor"
                            mys.GetSinglePhaseProp("phasefraction", labels(i), "Mole", res)
                            xv = res(0)
                        Case "Liquid"
                            mys.GetSinglePhaseProp("phasefraction", labels(i), "Mole", res)
                            xl1 = res(0)
                        Case "Liquid2"
                            mys.GetSinglePhaseProp("phasefraction", labels(i), "Mole", res)
                            xl2 = res(0)
                        Case "Solid"
                            mys.GetSinglePhaseProp("phasefraction", labels(i), "Mole", res)
                            xs = res(0)
                    End Select
                Else
                    Select Case labels(i)
                        Case "Vapor"
                            xv = 0.0#
                        Case "Liquid"
                            xl1 = 0.0#
                        Case "Liquid2"
                            xl2 = 0.0#
                        Case "Solid"
                            xs = 0.0#
                    End Select
                End If
            Next

            Me.CurrentMaterialStream = ms

            'copy fractions

            With ms
                i = 0
                For Each s As Interfaces.ICompound In .Phases(0).Compounds.Values
                    s.MoleFraction = Vz(i)
                    i += 1
                Next
                If Vy IsNot Nothing Then
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(2).Compounds.Values
                        s.MoleFraction = Vy(i)
                        i += 1
                    Next
                    Vwy = Me.AUX_CONVERT_MOL_TO_MASS(Vy)
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(2).Compounds.Values
                        s.MassFraction = Vwy(i)
                        i += 1
                    Next
                Else
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(2).Compounds.Values
                        s.MoleFraction = 0.0#
                        i += 1
                    Next
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(2).Compounds.Values
                        s.MassFraction = 0.0#
                        i += 1
                    Next
                End If
                .Phases(2).Properties.molarfraction = xv
                If Vx1 IsNot Nothing Then
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(3).Compounds.Values
                        s.MoleFraction = Vx1(i)
                        i += 1
                    Next
                    Vwx1 = Me.AUX_CONVERT_MOL_TO_MASS(Vx1)
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(3).Compounds.Values
                        s.MassFraction = Vwx1(i)
                        i += 1
                    Next
                Else
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(3).Compounds.Values
                        s.MoleFraction = 0.0#
                        i += 1
                    Next
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(3).Compounds.Values
                        s.MassFraction = 0.0#
                        i += 1
                    Next
                End If
                .Phases(3).Properties.molarfraction = xl1
                If Vx2 IsNot Nothing Then
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(4).Compounds.Values
                        s.MoleFraction = Vx2(i)
                        i += 1
                    Next
                    Vwx2 = Me.AUX_CONVERT_MOL_TO_MASS(Vx2)
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(4).Compounds.Values
                        s.MassFraction = Vwx2(i)
                        i += 1
                    Next
                Else
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(4).Compounds.Values
                        s.MoleFraction = 0.0#
                        i += 1
                    Next
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(4).Compounds.Values
                        s.MassFraction = 0.0#
                        i += 1
                    Next
                End If
                .Phases(4).Properties.molarfraction = xl2
                If Vs IsNot Nothing Then
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(7).Compounds.Values
                        s.MoleFraction = Vs(i)
                        i += 1
                    Next
                    Vws = Me.AUX_CONVERT_MOL_TO_MASS(Vs)
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(7).Compounds.Values
                        s.MassFraction = Vws(i)
                        i += 1
                    Next
                Else
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(7).Compounds.Values
                        s.MoleFraction = 0.0#
                        i += 1
                    Next
                    i = 0
                    For Each s As Interfaces.ICompound In .Phases(7).Compounds.Values
                        s.MassFraction = 0.0#
                        i += 1
                    Next
                End If
                .Phases(7).Properties.molarfraction = xs
            End With

            Return ms

        End Function

#End Region

#Region "   CAPE-OPEN ICapeUtilities Implementation"

        Public _availablecomps As Dictionary(Of String, Interfaces.ICompoundConstantProperties)
        Public _selectedcomps As Dictionary(Of String, Interfaces.ICompoundConstantProperties)

        <System.NonSerialized()> Friend _pme As Object

        ''' <summary>
        ''' The PMC displays its user interface and allows the Flowsheet User to interact with it. If no user interface is
        ''' available it returns an error.</summary>
        ''' <remarks></remarks>
        Public Overridable Sub Edit() Implements CapeOpen.ICapeUtilities.Edit
            'Dim cf As New FormConfigCAPEOPEN2
            'With cf
            '    ._pp = Me
            '    ._selcomps = _selectedcomps
            '    ._availcomps = _availablecomps
            '    .Show()
            '    _selectedcomps = ._selcomps
            '    _availablecomps = ._availcomps
            'End With
        End Sub

        ''' <summary>
        ''' Initially, this method was only present in the ICapeUnit interface. Since ICapeUtilities.Initialize is now
        ''' available for any kind of PMC, ICapeUnit. Initialize is deprecated.
        ''' The PME will order the PMC to get initialized through this method. Any initialisation that could fail must be
        ''' placed here. Initialize is guaranteed to be the first method called by the client (except low level methods such
        ''' as class constructors or initialization persistence methods). Initialize has to be called once when the PMC is
        ''' instantiated in a particular flowsheet.
        ''' When the initialization fails, before signalling an error, the PMC must free all the resources that were
        ''' allocated before the failure occurred. When the PME receives this error, it may not use the PMC anymore.
        ''' The method terminate of the current interface must not either be called. Hence, the PME may only release
        ''' the PMC through the middleware native mechanisms.
        ''' </summary>
        ''' <remarks></remarks>
        Public Overridable Sub Initialize() Implements CapeOpen.ICapeUtilities.Initialize

            Me.m_ip = New DataTable
            ConfigParameters()

            'load Henry Coefficients
            Dim pathsep = IO.Path.DirectorySeparatorChar
            Dim filename As String = My.Application.Info.DirectoryPath & pathsep & "data" & pathsep & "Henry.txt"
            Dim HenryLines() As String = IO.File.ReadAllLines(filename)

            For i = 2 To HenryLines.Length - 1
                Dim HP As New HenryParam
                HP.Component = HenryLines(i).Split(";")(1)
                HP.CAS = HenryLines(i).Split(";")(2)
                HP.KHcp = Val(HenryLines(i).Split(";")(3))
                HP.C = Val(HenryLines(i).Split(";")(4))
                m_Henry.Add(HP.CAS, HP)
            Next

        End Sub

        ''' <summary>
        ''' Returns an ICapeCollection interface.
        ''' </summary>
        ''' <value></value>
        ''' <returns>CapeInterface (ICapeCollection)</returns>
        ''' <remarks>This interface will contain a collection of ICapeParameter interfaces.
        ''' This method allows any client to access all the CO Parameters exposed by a PMC. Initially, this method was
        ''' only present in the ICapeUnit interface. Since ICapeUtilities.GetParameters is now available for any kind of
        ''' PMC, ICapeUnit.GetParameters is deprecated. Consult the “Open Interface Specification: Parameter
        ''' Common Interface” document for more information about parameter. Consult the “Open Interface
        ''' Specification: Collection Common Interface” document for more information about collection.
        ''' If the PMC does not support exposing its parameters, it should raise the ECapeNoImpl error, instead of
        ''' returning a NULL reference or an empty Collection. But if the PMC supports parameters but has for this call
        ''' no parameters, it should return a valid ICapeCollection reference exposing zero parameters.</remarks>
        Public Overridable ReadOnly Property parameters1() As Object Implements CapeOpen.ICapeUtilities.parameters
            Get
                Dim parms As New CapeOpen.ParameterCollection
                For Each kvp As KeyValuePair(Of String, Double) In Me.Parameters
                    parms.Add(New CapeOpen.RealParameter(kvp.Key, kvp.Key, kvp.Value, kvp.Value, Double.MinValue, Double.MaxValue, CapeParamMode.CAPE_OUTPUT, ""))
                Next
                Return parms
            End Get
        End Property

        ''' <summary>
        ''' Allows the PME to convey the PMC a reference to the former’s simulation context. 
        ''' </summary>
        ''' <value>The reference to the PME’s simulation context class. For the PMC to
        ''' use this class, this reference will have to be converted to each of the
        ''' defined CO Simulation Context interfaces.</value>
        ''' <remarks>The simulation context
        ''' will be PME objects which will expose a given set of CO interfaces. Each of these interfaces will allow the
        ''' PMC to call back the PME in order to benefit from its exposed services (such as creation of material
        ''' templates, diagnostics or measurement unit conversion). If the PMC does not support accessing the
        ''' simulation context, it is recommended to raise the ECapeNoImpl error.
        ''' Initially, this method was only present in the ICapeUnit interface. Since ICapeUtilities.SetSimulationContext
        ''' is now available for any kind of PMC, ICapeUnit. SetSimulationContext is deprecated.</remarks>
        Public Overridable WriteOnly Property simulationContext() As Object Implements CapeOpen.ICapeUtilities.simulationContext
            Set(ByVal value As Object)
                _pme = value
            End Set
        End Property

        ''' <summary>
        ''' Initially, this method was only present in the ICapeUnit interface. Since ICapeUtilities.Terminate is now
        ''' available for any kind of PMC, ICapeUnit.Terminate is deprecated.
        ''' The PME will order the PMC to get destroyed through this method. Any uninitialization that could fail must
        ''' be placed here. ‘Terminate’ is guaranteed to be the last method called by the client (except low level methods
        ''' such as class destructors). ‘Terminate’ may be called at any time, but may be only called once.
        ''' When this method returns an error, the PME should report the user. However, after that the PME is not
        ''' allowed to use the PMC anymore.
        ''' The Unit specification stated that “Terminate may check if the data has been saved and return an error if
        ''' not.” It is suggested not to follow this recommendation, since it’s the PME responsibility to save the state of
        ''' the PMC before terminating it. In the case that a user wants to close a simulation case without saving it, it’s
        ''' better to leave the PME to handle the situation instead of each PMC providing a different implementation.
        ''' </summary>
        ''' <remarks></remarks>
        Public Overridable Sub Terminate() Implements CapeOpen.ICapeUtilities.Terminate
            Me.CurrentMaterialStream = Nothing
            If _como IsNot Nothing Then System.Runtime.InteropServices.Marshal.ReleaseComObject(_como)
            Me.Dispose()
        End Sub

#End Region

#Region "   CAPE-OPEN Persistence Implementation"

        Private m_dirty As Boolean = True

        Public Sub GetClassID(ByRef pClassID As System.Guid) Implements IPersistStreamInit.GetClassID
            pClassID = New Guid(PropertyPackage.ClassId)
        End Sub

        Public Sub GetSizeMax(ByRef pcbSize As Long) Implements IPersistStreamInit.GetSizeMax
            pcbSize = 1024 * 1024
        End Sub

        Public Sub InitNew() Implements IPersistStreamInit.InitNew
            'do nothing
        End Sub

        Public Function IsDirty() As Integer Implements IPersistStreamInit.IsDirty
            Return m_dirty
        End Function

        Public Sub Load(ByVal pStm As System.Runtime.InteropServices.ComTypes.IStream) Implements IPersistStreamInit.Load

            ' Read the length of the string  
            Dim arrLen As Byte() = New [Byte](3) {}
            pStm.Read(arrLen, arrLen.Length, IntPtr.Zero)

            ' Calculate the length  
            Dim cb As Integer = BitConverter.ToInt32(arrLen, 0)

            ' Read the stream to get the string    
            Dim bytes As Byte() = New Byte(cb - 1) {}
            Dim pcb As New IntPtr()
            pStm.Read(bytes, bytes.Length, pcb)
            If System.Runtime.InteropServices.Marshal.IsComObject(pStm) Then System.Runtime.InteropServices.Marshal.ReleaseComObject(pStm)

            ' Deserialize byte array    

            Dim memoryStream As New System.IO.MemoryStream(bytes)

            Try

                Dim domain As AppDomain = AppDomain.CurrentDomain
                AddHandler domain.AssemblyResolve, New ResolveEventHandler(AddressOf MyResolveEventHandler)

                Dim myarr As ArrayList

                Dim mySerializer As Binary.BinaryFormatter = New Binary.BinaryFormatter(Nothing, New System.Runtime.Serialization.StreamingContext())
                myarr = mySerializer.Deserialize(memoryStream)

                _availablecomps = myarr(0)
                _selectedcomps = myarr(1)
                '_ioquick = myarr(2)
                _tpseverity = myarr(3)
                _tpcompids = TryCast(myarr(4), String())
                m_par = myarr(5)
                _flashalgorithm = myarr(6)

                Select Case Me.ComponentName
                    Case "PC-SAFT"
                        CType(Me, PCSAFTPropertyPackage).m_pr = myarr(7)
                    Case "Peng-Robinson (PR)"
                        CType(Me, PengRobinsonPropertyPackage).m_pr = myarr(7)
                    Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-M)", "Peng-Robinson-Stryjek-Vera 2 (PRSV2)"
                        CType(Me, PRSV2PropertyPackage).m_pr = myarr(7)
                    Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-VL)"
                        CType(Me, PRSV2VLPropertyPackage).m_pr = myarr(7)
                    Case "Soave-Redlich-Kwong (SRK)"
                        CType(Me, SRKPropertyPackage).m_pr = myarr(7)
                    Case "Peng-Robinson / Lee-Kesler (PR/LK)"
                        CType(Me, PengRobinsonLKPropertyPackage).m_pr = myarr(7)
                        CType(Me, PengRobinsonLKPropertyPackage).m_lk = myarr(8)
                    Case "UNIFAC"
                        CType(Me, UNIFACPropertyPackage).m_pr = myarr(7)
                    Case "UNIFAC-LL"
                        CType(Me, UNIFACLLPropertyPackage).m_pr = myarr(7)
                    Case "NRTL"
                        CType(Me, NRTLPropertyPackage).m_pr = myarr(7)
                        CType(Me, NRTLPropertyPackage).m_uni = myarr(8)
                    Case "UNIQUAC"
                        CType(Me, UNIQUACPropertyPackage).m_pr = myarr(7)
                        CType(Me, UNIQUACPropertyPackage).m_uni = myarr(8)
                    Case "Modified UNIFAC (Dortmund)"
                        CType(Me, MODFACPropertyPackage).m_pr = myarr(7)
                    Case "Chao-Seader"
                        CType(Me, ChaoSeaderPropertyPackage).m_pr = myarr(7)
                        CType(Me, ChaoSeaderPropertyPackage).m_lk = myarr(8)
                        CType(Me, ChaoSeaderPropertyPackage).m_cs = myarr(9)
                    Case "Grayson-Streed"
                        CType(Me, GraysonStreedPropertyPackage).m_pr = myarr(7)
                        CType(Me, GraysonStreedPropertyPackage).m_lk = myarr(8)
                        CType(Me, GraysonStreedPropertyPackage).m_cs = myarr(9)
                    Case "Lee-Kesler-Plöcker"
                        CType(Me, LKPPropertyPackage).m_pr = myarr(7)
                        CType(Me, LKPPropertyPackage).m_lk = myarr(8)
                    Case "Raoult's Law", "IAPWS-IF97 Steam Tables"
                End Select

                myarr = Nothing
                mySerializer = Nothing

                RemoveHandler domain.AssemblyResolve, New ResolveEventHandler(AddressOf MyResolveEventHandler)

            Catch p_Ex As System.Exception

                Throw p_Ex

            End Try

            memoryStream.Close()

        End Sub

        Public Sub Save(ByVal pStm As System.Runtime.InteropServices.ComTypes.IStream, ByVal fClearDirty As Boolean) Implements IPersistStreamInit.Save

            Dim props As New ArrayList

            With props

                .Add(_availablecomps)
                .Add(_selectedcomps)
                .Add("")
                .Add(_tpseverity)
                .Add(_tpcompids)
                .Add(m_par)
                .Add(_flashalgorithm)

                Select Case Me.ComponentName
                    Case "PC-SAFT"
                        .Add(CType(Me, PCSAFTPropertyPackage).m_pr)
                    Case "Peng-Robinson (PR)"
                        .Add(CType(Me, PengRobinsonPropertyPackage).m_pr)
                    Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-M)", "Peng-Robinson-Stryjek-Vera 2 (PRSV2)"
                        .Add(CType(Me, PRSV2PropertyPackage).m_pr)
                    Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-VL)"
                        .Add(CType(Me, PRSV2VLPropertyPackage).m_pr)
                    Case "Soave-Redlich-Kwong (SRK)"
                        .Add(CType(Me, SRKPropertyPackage).m_pr)
                    Case "Peng-Robinson / Lee-Kesler (PR/LK)"
                        .Add(CType(Me, PengRobinsonLKPropertyPackage).m_pr)
                        .Add(CType(Me, PengRobinsonLKPropertyPackage).m_lk)
                    Case "UNIFAC"
                        .Add(CType(Me, UNIFACPropertyPackage).m_pr)
                    Case "UNIFAC-LL"
                        .Add(CType(Me, UNIFACLLPropertyPackage).m_pr)
                    Case "NRTL"
                        .Add(CType(Me, NRTLPropertyPackage).m_pr)
                        .Add(CType(Me, NRTLPropertyPackage).m_uni)
                    Case "UNIQUAC"
                        .Add(CType(Me, UNIQUACPropertyPackage).m_pr)
                        .Add(CType(Me, UNIQUACPropertyPackage).m_uni)
                    Case "Modified UNIFAC (Dortmund)"
                        .Add(CType(Me, MODFACPropertyPackage).m_pr)
                    Case "Chao-Seader"
                        .Add(CType(Me, ChaoSeaderPropertyPackage).m_pr)
                        .Add(CType(Me, ChaoSeaderPropertyPackage).m_lk)
                        .Add(CType(Me, ChaoSeaderPropertyPackage).m_cs)
                    Case "Grayson-Streed"
                        .Add(CType(Me, GraysonStreedPropertyPackage).m_pr)
                        .Add(CType(Me, GraysonStreedPropertyPackage).m_lk)
                        .Add(CType(Me, GraysonStreedPropertyPackage).m_cs)
                    Case "Lee-Kesler-Plöcker"
                        .Add(CType(Me, LKPPropertyPackage).m_pr)
                        .Add(CType(Me, LKPPropertyPackage).m_lk)
                    Case "Raoult's Law", "IAPWS-IF97 Steam Tables"
                End Select

            End With

            Dim mySerializer As Binary.BinaryFormatter = New Binary.BinaryFormatter(Nothing, New System.Runtime.Serialization.StreamingContext())
            Dim mstr As New MemoryStream
            mySerializer.Serialize(mstr, props)
            Dim bytes As Byte() = mstr.ToArray()
            mstr.Close()

            ' construct length (separate into two separate bytes)    

            Dim arrLen As Byte() = BitConverter.GetBytes(bytes.Length)
            Try

                ' Save the array in the stream    
                pStm.Write(arrLen, arrLen.Length, IntPtr.Zero)
                pStm.Write(bytes, bytes.Length, IntPtr.Zero)
                If System.Runtime.InteropServices.Marshal.IsComObject(pStm) Then System.Runtime.InteropServices.Marshal.ReleaseComObject(pStm)

            Catch p_Ex As System.Exception

                Throw p_Ex

            End Try

            If fClearDirty Then
                m_dirty = False
            End If

        End Sub

        Private Function MyResolveEventHandler(ByVal sender As Object, ByVal args As ResolveEventArgs) As System.Reflection.Assembly
            Return Me.[GetType]().Assembly
        End Function

#End Region

#Region "   CAPE-OPEN Error Interfaces"

        Sub ThrowCAPEException(ByRef ex As Exception, ByVal name As String, ByVal description As String, ByVal interf As String, ByVal moreinfo As String, ByVal operation As String, ByVal scope As String, ByVal code As Integer)

            _name = name
            _code = code
            _description = description
            _interfacename = interf
            _moreinfo = moreinfo
            _operation = operation
            _scope = scope

            Throw ex

        End Sub

        Private _name, _description, _interfacename, _moreinfo, _operation, _scope As String, _code As Integer

        Public ReadOnly Property Name() As String Implements CapeOpen.ECapeRoot.Name, IPropertyPackage.Name
            Get
                Return _name
            End Get
        End Property

        Public ReadOnly Property code() As Integer Implements CapeOpen.ECapeUser.code
            Get
                Return _code
            End Get
        End Property

        Public ReadOnly Property description() As String Implements CapeOpen.ECapeUser.description
            Get
                Return _description
            End Get
        End Property

        Public ReadOnly Property interfaceName() As String Implements CapeOpen.ECapeUser.interfaceName
            Get
                Return _interfacename
            End Get
        End Property

        Public ReadOnly Property moreInfo() As String Implements CapeOpen.ECapeUser.moreInfo
            Get
                Return _moreinfo
            End Get
        End Property

        Public ReadOnly Property operation() As String Implements CapeOpen.ECapeUser.operation
            Get
                Return _operation
            End Get
        End Property

        Public ReadOnly Property scope() As String Implements CapeOpen.ECapeUser.scope
            Get
                Return _scope
            End Get
        End Property

#End Region

#Region "   IDisposable Support "
        Private disposedValue As Boolean = False        ' To detect redundant calls

        ' IDisposable
        Protected Overridable Sub Dispose(ByVal disposing As Boolean)
            If Not Me.disposedValue Then
                If disposing Then
                    ' TODO: free other state (managed objects).
                End If

                ' TODO: free your own state (unmanaged objects).
                If _como IsNot Nothing Then
                    If System.Runtime.InteropServices.Marshal.IsComObject(_como) Then
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(_como)
                    Else
                        _como = Nothing
                    End If
                End If
                If _pme IsNot Nothing Then
                    If System.Runtime.InteropServices.Marshal.IsComObject(_pme) Then
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(_pme)
                    Else
                        _pme = Nothing
                    End If
                End If


                ' TODO: set large fields to null.
            End If
            Me.disposedValue = True
        End Sub

#Region " IDisposable Support "
        ' This code added by Visual Basic to correctly implement the disposable pattern.
        Public Overridable Sub Dispose() Implements IDisposable.Dispose
            ' Do not change this code.  Put cleanup code in Dispose(ByVal disposing As Boolean) above.
            Dispose(True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

#End Region

#Region "   XML data persistence"

        Public Overridable Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean Implements XMLSerializer.Interfaces.ICustomXMLSerialization.LoadData

            Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture

            Me.UniqueID = (From el As XElement In data Select el Where el.Name = "ID").SingleOrDefault.Value
            Me.ComponentName = (From el As XElement In data Select el Where el.Name = "ComponentName").SingleOrDefault.Value
            Me.ComponentDescription = (From el As XElement In data Select el Where el.Name = "ComponentDescription").SingleOrDefault.Value
            Me.Tag = (From el As XElement In data Select el Where el.Name = "Tag").SingleOrDefault.Value
            Me._tpseverity = (From el As XElement In data Select el Where el.Name = "TPSeverity").SingleOrDefault.Value
            Me._tpcompids = XMLSerializer.XMLSerializer.StringToArray2((From el As XElement In data Select el Where el.Name = "TPCompIDs").SingleOrDefault.Value, ci, Type.GetType("System.String"))

            For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "Parameters").SingleOrDefault.Elements.ToList
                If m_par.ContainsKey(xel.@ID) Then m_par(xel.@ID) = Double.Parse(xel.@Value, ci) Else m_par.Add(xel.@ID, Double.Parse(xel.@Value, ci))
            Next

            Select Case Me.ComponentName

                Case "PC-SAFT"

                    Dim pp As PCSAFTPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PCSIP() With {.compound1 = xel.@Compound1, .compound2 = xel.@Compound2, .casno1 = xel.@Compound1, .casno2 = xel.@Compound2, .kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PCSIP)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "Peng-Robinson (PR)"

                    Dim pp As PengRobinsonPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-M)", "Peng-Robinson-Stryjek-Vera 2 (PRSV2)"

                    Dim pp As PRSV2PropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PRSV2_IPData() With {.id1 = xel.@Compound1, .id2 = xel.@Compound2, .kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PRSV2_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-VL)"

                    Dim pp As PRSV2VLPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PRSV2_IPData() With {.id1 = xel.@Compound1, .id2 = xel.@Compound2, .kij = Double.Parse(xel.@kij, ci), .kji = Double.Parse(xel.@kji, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PRSV2_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "Soave-Redlich-Kwong (SRK)"

                    Dim pp As SRKPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "Peng-Robinson / Lee-Kesler (PR/LK)"

                    Dim pp As PengRobinsonLKPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next


                Case "UNIFAC"

                    Dim pp As UNIFACPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "UNIFAC-LL"

                    Dim pp As UNIFACLLPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "NRTL"

                    Dim pp As NRTLPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters_PR").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                    'pp.m_uni.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters_NRTL").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.NRTL_IPData() With {.ID1 = xel.@ID1, .ID2 = xel.@ID2, .A12 = Double.Parse(xel.@A12, ci), .A21 = Double.Parse(xel.@A21, ci),
                                                                    .B12 = Double.Parse(xel.@B12, ci), .B21 = Double.Parse(xel.@B21, ci),
                                                                    .C12 = Double.Parse(xel.@C12, ci), .C21 = Double.Parse(xel.@C21, ci),
                                                                    .alpha12 = Double.Parse(xel.@alpha12, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.NRTL_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_uni.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_uni.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_uni.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_uni.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_uni.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "UNIQUAC"

                    Dim pp As UNIQUACPropertyPackage = Me

                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters_PR").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                    'pp.m_uni.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters_UNIQUAC").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.UNIQUAC_IPData() With {.ID1 = xel.@ID1, .ID2 = xel.@ID2, .A12 = Double.Parse(xel.@A12, ci), .A21 = Double.Parse(xel.@A21, ci),
                                                                       .B12 = Double.Parse(xel.@B12, ci), .B21 = Double.Parse(xel.@B21, ci),
                                                                       .C12 = Double.Parse(xel.@C12, ci), .C21 = Double.Parse(xel.@C21, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.UNIQUAC_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_uni.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_uni.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_uni.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_uni.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_uni.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

                Case "Modified UNIFAC (Dortmund)"

                    Dim pp As MODFACPropertyPackage = Me
                    'pp.m_pr.InteractionParameters.Clear()
                    For Each xel As XElement In (From xel2 As XElement In data Select xel2 Where xel2.Name = "InteractionParameters").SingleOrDefault.Elements.ToList
                        Dim ip As New Auxiliary.PR_IPData() With {.kij = Double.Parse(xel.@Value, ci)}
                        Dim dic As New Dictionary(Of String, Auxiliary.PR_IPData)
                        dic.Add(xel.@Compound2, ip)
                        If Not pp.m_pr.InteractionParameters.ContainsKey(xel.@Compound1) Then
                            pp.m_pr.InteractionParameters.Add(xel.@Compound1, dic)
                        Else
                            If Not pp.m_pr.InteractionParameters(xel.@Compound1).ContainsKey(xel.@Compound2) Then
                                pp.m_pr.InteractionParameters(xel.@Compound1).Add(xel.@Compound2, ip)
                            Else
                                pp.m_pr.InteractionParameters(xel.@Compound1)(xel.@Compound2) = ip
                            End If
                        End If
                    Next

            End Select

        End Function

        Public Overridable Function SaveData() As System.Collections.Generic.List(Of System.Xml.Linq.XElement) Implements XMLSerializer.Interfaces.ICustomXMLSerialization.SaveData

            Dim elements As New System.Collections.Generic.List(Of System.Xml.Linq.XElement)
            Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture

            With elements

                .Add(New XElement("Type", Me.GetType.ToString))
                .Add(New XElement("ComponentName", ComponentName))
                .Add(New XElement("ComponentDescription", ComponentDescription))
                .Add(New XElement("Tag", Tag))
                .Add(New XElement("TPSeverity", _tpseverity))
                .Add(New XElement("TPCompIDs", XMLSerializer.XMLSerializer.ArrayToString2(_tpcompids, ci)))

                .Add(New XElement("Parameters"))

                For Each kvp As KeyValuePair(Of String, Double) In m_par
                    elements(elements.Count - 1).Add(New XElement("Parameter", {New XAttribute("ID", kvp.Key), New XAttribute("Value", kvp.Value.ToString(ci))}))
                Next

                Select Case Me.ComponentName
                    Case "PC-SAFT"

                        Dim pp As PCSAFTPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))
                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PCSIP)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PCSIP) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                           New XAttribute("Compound2", kvp2.Key),
                                                                           New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Peng-Robinson (PR)"

                        Dim pp As PengRobinsonPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))
                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                        New XAttribute("Compound2", kvp2.Key),
                                                                        New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-M)", "Peng-Robinson-Stryjek-Vera 2 (PRSV2)"

                        Dim pp As PRSV2PropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))
                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PRSV2_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PRSV2_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                        New XAttribute("Compound2", kvp2.Key),
                                                                        New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Peng-Robinson-Stryjek-Vera 2 (PRSV2-VL)"

                        Dim pp As PRSV2VLPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))
                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PRSV2_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PRSV2_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                        New XAttribute("Compound2", kvp2.Key),
                                                                        New XAttribute("kij", kvp2.Value.kij.ToString(ci)),
                                                                        New XAttribute("kji", kvp2.Value.kji.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Soave-Redlich-Kwong (SRK)"

                        Dim pp As SRKPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))
                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                      New XAttribute("Compound2", kvp2.Key),
                                                                      New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Peng-Robinson / Lee-Kesler (PR/LK)"

                        Dim pp As PengRobinsonLKPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                           New XAttribute("Compound2", kvp2.Key),
                                                                           New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "UNIFAC"

                        Dim pp As UNIFACPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                        New XAttribute("Compound2", kvp2.Key),
                                                                        New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "UNIFAC-LL"

                        Dim pp As UNIFACLLPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                          New XAttribute("Compound2", kvp2.Key),
                                                                          New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "NRTL"

                        Dim pp As NRTLPropertyPackage = Me

                        .Add(New XElement("InteractionParameters_PR"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                        New XAttribute("Compound2", kvp2.Key),
                                                                        New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                        .Add(New XElement("InteractionParameters_NRTL"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.NRTL_IPData)) In pp.m_uni.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.NRTL_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                         New XAttribute("Compound2", kvp2.Key),
                                                                         New XAttribute("ID1", kvp2.Value.ID1),
                                                                         New XAttribute("ID2", kvp2.Value.ID2),
                                                                         New XAttribute("A12", kvp2.Value.A12.ToString(ci)),
                                                                         New XAttribute("A21", kvp2.Value.A21.ToString(ci)),
                                                                         New XAttribute("B12", kvp2.Value.B12.ToString(ci)),
                                                                         New XAttribute("B21", kvp2.Value.B21.ToString(ci)),
                                                                         New XAttribute("C12", kvp2.Value.C12.ToString(ci)),
                                                                         New XAttribute("C21", kvp2.Value.C21.ToString(ci)),
                                                                         New XAttribute("alpha12", kvp2.Value.alpha12.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "UNIQUAC"

                        Dim pp As UNIQUACPropertyPackage = Me

                        .Add(New XElement("InteractionParameters_PR"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                      New XAttribute("Compound2", kvp2.Key),
                                                                      New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                        .Add(New XElement("InteractionParameters_UNIQUAC"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.UNIQUAC_IPData)) In pp.m_uni.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.UNIQUAC_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                      New XAttribute("Compound2", kvp2.Key),
                                                                      New XAttribute("ID1", kvp2.Value.ID1),
                                                                      New XAttribute("ID2", kvp2.Value.ID2),
                                                                      New XAttribute("A12", kvp2.Value.A12.ToString(ci)),
                                                                      New XAttribute("A21", kvp2.Value.A21.ToString(ci)),
                                                                      New XAttribute("B12", kvp2.Value.B12.ToString(ci)),
                                                                      New XAttribute("B21", kvp2.Value.B21.ToString(ci)),
                                                                      New XAttribute("C12", kvp2.Value.C12.ToString(ci)),
                                                                      New XAttribute("C21", kvp2.Value.C21.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Modified UNIFAC (Dortmund)"

                        Dim pp As MODFACPropertyPackage = Me

                        .Add(New XElement("InteractionParameters"))

                        For Each kvp As KeyValuePair(Of String, Dictionary(Of String, Auxiliary.PR_IPData)) In pp.m_pr.InteractionParameters
                            For Each kvp2 As KeyValuePair(Of String, Auxiliary.PR_IPData) In kvp.Value
                                If Not Me.CurrentMaterialStream Is Nothing Then
                                    If Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp.Key) And Me.CurrentMaterialStream.Phases(0).Compounds.ContainsKey(kvp2.Key) Then
                                        .Item(.Count - 1).Add(New XElement("InteractionParameter", New XAttribute("Compound1", kvp.Key),
                                                                     New XAttribute("Compound2", kvp2.Key),
                                                                     New XAttribute("Value", kvp2.Value.kij.ToString(ci))))
                                    End If
                                End If
                            Next
                        Next

                    Case "Chao-Seader"
                    Case "Grayson-Streed"
                    Case "Lee-Kesler-Plöcker"
                    Case "Raoult's Law", "IAPWS-IF97 Steam Tables"

                End Select

            End With

            Return elements

        End Function

#End Region

        Public Function AUX_CPm1(phase As Enums.PhaseLabel, Ti As Double) As Double Implements IPropertyPackage.AUX_CPm

            Select Case phase
                Case Enums.PhaseLabel.Vapor
                    Return AUX_CPm(PropertyPackages.Phase.Vapor, Ti)
                Case Enums.PhaseLabel.Solid
                    Return AUX_CPm(PropertyPackages.Phase.Solid, Ti)
                Case Enums.PhaseLabel.Mixture
                    Return AUX_CPm(PropertyPackages.Phase.Mixture, Ti)
                Case Enums.PhaseLabel.LiquidMixture
                    Return AUX_CPm(PropertyPackages.Phase.Liquid, Ti)
                Case Else
                    Return AUX_CPm(PropertyPackages.Phase.Mixture, Ti)
            End Select

        End Function

        Public Function AUX_MMM1(phase As Enums.PhaseLabel) As Double Implements IPropertyPackage.AUX_MMM

            Select Case phase
                Case Enums.PhaseLabel.Vapor
                    Return AUX_MMM1(PropertyPackages.Phase.Vapor)
                Case Enums.PhaseLabel.Solid
                    Return AUX_MMM1(PropertyPackages.Phase.Solid)
                Case Enums.PhaseLabel.Mixture
                    Return AUX_MMM1(PropertyPackages.Phase.Mixture)
                Case Enums.PhaseLabel.LiquidMixture
                    Return AUX_MMM1(PropertyPackages.Phase.Liquid)
                Case Else
                    Return AUX_MMM1(PropertyPackages.Phase.Mixture)
            End Select
        End Function

    End Class

    ''' <summary>
    ''' Class to store Phase Info and mapping for CAPE-OPEN Property Packages
    ''' </summary>
    ''' <remarks>Used only in the context of CAPE-OPEN Objects.</remarks>
    <System.Serializable()> Public Class PhaseInfo

        Public PhaseLabel As String = ""
        Public DWPhaseIndex As Integer
        Public DWPhaseID As Phase = Phase.Mixture

        Sub New(ByVal pl As String, ByVal pi As Integer, ByVal pid As Phase)
            PhaseLabel = pl
            DWPhaseIndex = pi
            DWPhaseID = pid
        End Sub

    End Class

    ''' <summary>
    ''' COM IStream Class Implementation
    ''' </summary>
    ''' <remarks></remarks>
    <System.Serializable()> Public Class ComStreamWrapper
        Inherits System.IO.Stream
        Private mSource As IStream
        Private mInt64 As IntPtr

        Public Sub New(ByVal source As IStream)
            mSource = source
            mInt64 = iop.Marshal.AllocCoTaskMem(8)
        End Sub

        Protected Overrides Sub Finalize()
            Try
                iop.Marshal.FreeCoTaskMem(mInt64)
            Finally
                MyBase.Finalize()
            End Try
        End Sub

        Public Overrides ReadOnly Property CanRead() As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek() As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite() As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Sub Flush()
            mSource.Commit(0)
        End Sub

        Public Overrides ReadOnly Property Length() As Long
            Get
                Dim stat As STATSTG
                stat = Nothing
                mSource.Stat(stat, 1)
                Return stat.cbSize
            End Get
        End Property

        Public Overrides Property Position() As Long
            Get
                Throw New NotImplementedException()
            End Get
            Set(ByVal value As Long)
                Throw New NotImplementedException()
            End Set
        End Property

        Public Overrides Function Read(ByVal buffer As Byte(), ByVal offset As Integer, ByVal count As Integer) As Integer
            If offset <> 0 Then
                Throw New NotImplementedException()
            End If
            mSource.Read(buffer, count, mInt64)
            Return iop.Marshal.ReadInt32(mInt64)
        End Function

        Public Overrides Function Seek(ByVal offset As Long, ByVal origin As System.IO.SeekOrigin) As Long
            mSource.Seek(offset, Convert.ToInt32(origin), mInt64)
            Return iop.Marshal.ReadInt64(mInt64)
        End Function

        Public Overrides Sub SetLength(ByVal value As Long)
            mSource.SetSize(value)
        End Sub

        Public Overrides Sub Write(ByVal buffer As Byte(), ByVal offset As Integer, ByVal count As Integer)
            If offset <> 0 Then
                Throw New NotImplementedException()
            End If
            mSource.Write(buffer, count, IntPtr.Zero)
        End Sub

    End Class

    <System.Serializable()> Public Class HenryParam
        Public Component As String
        Public CAS As String
        Public KHcp As Double
        Public C As Double
    End Class

End Namespace