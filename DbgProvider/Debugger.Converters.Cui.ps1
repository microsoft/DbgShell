
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest


    # This could return null if it doesn't need to do anything.
    #
    # TODO: might be nice to actually export this func somewhere
    function GetRealTypeForCnDefType( $cnDefType, [string] $skipIfAlreadyThisType )
    {
        $defTypeAddr = $cnDefType.DbgGetPointer()
        [UInt64] $offset = 0

        $defTypeSym = $null
        try
        {
            $defTypeSym = $Debugger.GetSymbolByAddress( $defTypeAddr, [ref] $offset )
        }
        catch
        {
            # ignore it
        }

        if( !$defTypeSym -or (0 -ne $offset) )
        {
            # We could be looking at garbage.
            return $null
        }

        $realTypeName = $defTypeSym.Name
        $idx = $realTypeName.IndexOf( '$R::s_defType' )

        if( $idx -lt 0 )
        {
            # We could be looking at garbage.
            return $null
        }

        $realTypeName = $realTypeName.Remove( $idx )

        # Note that we have a special case for System::Object--if that's what the
        # CN type system says it is, then we want to use that explicitly, rather
        # than falling back onto derived type detection, because we know that DTD
        # is pretty much guaranteed to get it wrong (due to vtables being
        # collapsed by COMDAT folding, DTD will think it's an
        # AccessViolationException object or somesuch).
        #
        # IDEA: maybe we should just disable DTD for all System::Object-derived
        # types.
        if( ($realTypeName -eq $skipIfAlreadyThisType) -and ($realTypeName -ne 'System::Object') )
        {
            # We don't always need to use the CN metadata--we might already
            # have the right type.
            return $null
        }

        $realType = dt "CoreMessaging!$realTypeName"

        if( !$realType )
        {
            # I don't know why, but sometimes we don't get certain types in the PDB.
            # For instance, Microsoft::CoreUI::MessagingInterop::EndpointHandler.
            Write-Warning "CN metadata says the type is '$realTypeName', but we can't find that type."
            # We'll have to just throw ourselves on the mercy of normal Derived Type
            # Detection.
        }

        return $realType
    } # end GetRealTypeForCnDefType


    function IsCnValueType( $cnDefType )
    {
        if( 0 -ne ($cnDefType.nFlags -band 0x02) )
        {
            return $true
        }

        return $false

        # apparently we can also deduce this by looking for a "(type)__Boxed" type, in the
        # event that we don't have the memory of the $cnDefType.
    }



    # Derived type detection /almost/ does the trick for CNatural objects. Unfortunately,
    # "COMDAT folding" allows the linker to fold vtables, so for instance a
    # System::InvalidOperationException might show up as a
    # System::AccessViolationException (using the normal Derived Type Detection technique
    # of looking at what vtable is actually there). Fortunately, CN objects have their own
    # metadata, and we can use that to find out the for-sure proper type. (This is done in
    # the same way that !uiext.o does it.)
    New-DbgValueConverterInfo -TypeName 'CoreMessaging!System::Object' -Converter {
        try
        {
            # This could return null if it doesn't need to do anything.
            function MaybeChangeTypeBasedOnCnMetadata( $stockValue )
            {
                $realType = GetRealTypeForCnDefType $stockValue.cn.m_pdefType $sym.Type.Name

                if( !$realType )
                {
                    return $null
                }

                <# assert !$sym.IsValueInRegister, because you couldn't fit a System::Object into a register #>

                # The -NoConversion and -NoDerivedTypeDetection is because we want to
                # avoid infinite recursion (by calling into this converter again) and to
                # avoid undoing what we just did.
                return (Get-DbgSymbolValue -Address $sym.Address `
                                           -Type $realType `
                                           -NameForNewSymbol $sym.Name `
                                           -NoConversion `
                                           -NoDerivedTypeDetection)
            } # end MaybeChangeTypeBasedOnCnMetadata()


            # We'll use this to add some synthetic properties (stack trace).
            function AddStuffToException( $val )
            {
                [int] $idx = 0

                if( $val.m_cFrames -gt $val.m_rgpFrames.Count )
                {
                    # Bad data.
                    return $val
                }

                $frames = $val.m_rgpFrames[ 0 .. ($val.m_cFrames - 1) ] | `
                          # Get-DbgNearSymbol (ln) with a -Delta of 0 is basically like
                          # $debugger.GetSymbolByAddress.
                          ln -delta 0 | `
                          # Note that we need to negate the displacement property.
                          Select-Object -Property @( 'BaseAddress'
                                                     'Symbol'
                                                     @{ Name = 'Displacement'
                                                        Expression = { -$_.Displacement } } ) | %{
                              # Add a "TypeName" for the formatter to key off of:
                              $_.PSObject.TypeNames.insert( 0, 'StackFrameLite' )
                              # And an "index" property:
                              Add-Member -InputObject $_ -MemberType 'NoteProperty' -Name 'Index' -Value ($idx++)
                              $_
                          }

                Add-Member -InputObject $val `
                           -MemberType 'NoteProperty' `
                           -Name 'Stack' `
                           -Value $frames

                return $val
            } # end AddStuffToException()


            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetValue( $true, $true ) # skip both conversion AND derived type detection

            $val = MaybeChangeTypeBasedOnCnMetadata $stockValue

            # If it is a System::Exception, we'd like to do more stuff to it.
            if( $stockValue.PSObject.TypeNames.Contains( '!System::Exception' ) )
            {
                if( $null -eq $val )
                {
                    # If MaybeChangeTypeBasedOnCnMetadata returned null, that means it didn't
                    # do anything. So we'll need to come up with a value for
                    # AddStuffToException to work on.

                    # N.B. We make a copy of the stock value, because it would be really bad if we
                    # modified the stock value!
                    # $_ is the symbol
                    $val = $stockValue.PSObject.Copy()

                    # (if $val is already non-null, then it's okay for us to modify it)
                }

                $val = AddStuffToException $val
            }

            return $val
        }
        finally { }
    } -CaptureContext # end System::Object converter



    New-DbgValueConverterInfo -TypeName 'CoreMessaging!System::Array' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetValue( $true, $true ) # skip both conversion AND derived type detection

            if( $stockValue.m_cRank -ne 1 )
            {
                # TODO
                Write-Warning "could not get rank"
                return $null
            }

            $count = $stockValue.m_cItems
            if( $count -gt 8192 )
            {
                # Seems likely to be garbage?
                return $null
            }

            $elemType = GetRealTypeForCnDefType $stockValue.m_pdefElement

            if( !$elemType )
            {
                return $null
            }


            # Hmm... should I be creating a DbgArrayValue somehow instead?

            $val = New-Object 'object[]' -Arg @( $count )

            $curAddr = ($stockValue.m_rgpvStorage.DbgGetSymbol().Address)
            [UInt64] $increment = 0

            $isValueType = IsCnValueType $stockValue.m_pdefElement

            if( $isValueType )
            {
                $increment = $elemType.Size # do we have to worry about padding here?
            }
            else
            {
                $increment = $Debugger.PointerSize
                $elemType = $elemType.GetPointerType()
            }

            for( [int] $i = 0; $i -lt $count; $i++ )
            {
                $val[ $i ] = (Get-DbgSymbolValue -Address $curAddr `
                                                 -Type $elemType `
                                                 -NameForNewSymbol ($sym.Name + "[ $i ]") `
                                                 -NoConversion `
                                                 -NoDerivedTypeDetection)

                $curAddr += $increment
            }

            return $val
        }
        finally { }
    } -CaptureContext # end System::Array converter


    New-DbgValueConverterInfo -TypeName '!Cn::Com::ExportAdapter' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()

            $gcEntry = $stockValue.m_pEntry;

            if( $gcEntry.Context.DbgIsNull() )
            {
                # "orphaned"
                return $null
            }

            if( $gcEntry.Subject.DbgIsNull() )
            {
                # Should not happen... but we'll treat it as "orphaned". (Perhaps garbage
                # data.)
                return $null
            }

            # N.B. We make a copy of the ... is this right?
            #$val = $gcEntry.Subject.PSObject.Copy()
            $val = $gcEntry.Subject.DbgFollowPointers().PSObject.Copy()

            # People might want to take a look at the export adapter object, though...
            # (for instance to look at the m_cAdapterRef) Let's add it on as a synthetic
            # member.
            Add-Member -InputObject $val -MemberType 'NoteProperty' -Name 'ExportAdapter' -Value $stockValue

            return $val
        }
        finally { }
    } # end ExportAdapter converter


    # IntPtr is similarly unnecessary type encapsulation (at debugging time).
    New-DbgValueConverterInfo -TypeName 'CoreMessaging!System::IntPtr' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()
            return $stockValue.m_pvValue
        }
        finally { }
    } # end IntPtr converter


    New-DbgValueConverterInfo -TypeName @( 'CoreMessaging!System::Guid', '!Cn::Guid' ) -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            return $sym.ReadAs_Guid()
        }
        finally { }
    } # end Guid converter


    # Unfortunately there is no direct, symbolic way to recognize a CNatural enum type, so
    # we'll hardcode a list of known enum types.
    #
    # The indirect way to recognize a CN enum is: it's a UDT, with no base classes, with
    # two members "raw__" and "value__", and the type name of the "raw__" member ends with
    # '$C::Values'. DbgShell currently has no way to register a value converter using that
    # info.
    #
    # Fortunately, it's not a big deal to update this converter with new enum type names
    # as needed. Here's an example of a handy command that can dump the needed names:
    #
    #    Get-DbgTypeInfo -Name 'CoreMessaging!*$C::Values' | select -ExpandProperty Name | %{
    #        $_.Replace( '$C::Values', '' )
    #    } | sort
    #
    New-DbgValueConverterInfo -TypeName @(
        '!Cn::Threading::InterconnectBufferFlags'
        '!Microsoft::CoreUI::CloseEndpointFlags'
        '!Microsoft::CoreUI::ConversationConnectionMode'
        '!Microsoft::CoreUI::ConversationItemOwner'
        '!Microsoft::CoreUI::Conversations::Conversation__ConnectionState'
        '!Microsoft::CoreUI::Conversations::ConversationPeerID'
        '!Microsoft::CoreUI::Conversations::ItemGroupOwner'
        '!Microsoft::CoreUI::ConversationServerStyle'
        '!Microsoft::CoreUI::CustomDisposeFlags'
        '!Microsoft::CoreUI::Dispatch::DeferredCall__CallType'
        '!Microsoft::CoreUI::Dispatch::Dispatcher__LoopExitState'
        '!Microsoft::CoreUI::Dispatch::DispatchState'
        '!Microsoft::CoreUI::Dispatch::DispatchTrigger__TriggerOps'
        '!Microsoft::CoreUI::Dispatch::ExternalPriority'
        '!Microsoft::CoreUI::Dispatch::FlushMode'
        '!Microsoft::CoreUI::Dispatch::InternalPriority'
        '!Microsoft::CoreUI::Dispatch::OffThreadReceiveMode'
        '!Microsoft::CoreUI::Dispatch::RunMode'
        '!Microsoft::CoreUI::Dispatch::RunnablePriorityMask'
        '!Microsoft::CoreUI::Dispatch::ThreadContext__NotificationMessage'
        '!Microsoft::CoreUI::Dispatch::ThreadPriority'
        '!Microsoft::CoreUI::Dispatch::ThreadWakeReason'
        '!Microsoft::CoreUI::Dispatch::UserAdapter__UserPriority'
        '!Microsoft::CoreUI::Dispatch::WaitArray__ArrayManipulationMode'
        '!Microsoft::CoreUI::Dispatch::WaitKind'
        '!Microsoft::CoreUI::Dispatch::WaitStatus'
        '!Microsoft::CoreUI::ErrorSubsystems'
        '!Microsoft::CoreUI::ExternalRegistrarScope'
        '!Microsoft::CoreUI::HRESOURCE'
        '!Microsoft::CoreUI::Identity::HandleFreeReason'
        '!Microsoft::CoreUI::Messaging::AlpcSender__PendingConnectionType'
        '!Microsoft::CoreUI::Messaging::Connection__TargetLocation'
        '!Microsoft::CoreUI::Messaging::EndpointValidationFlags'
        '!Microsoft::CoreUI::Messaging::FlushPolicy'
        '!Microsoft::CoreUI::Messaging::MessageSession__ShutdownState'
        '!Microsoft::CoreUI::Messaging::MessagingErrors'
        '!Microsoft::CoreUI::Messaging::MessagingResults'
        '!Microsoft::CoreUI::ProcessID'
        '!Microsoft::CoreUI::ProtocolError'
        '!Microsoft::CoreUI::RawHRESULT'
        '!Microsoft::CoreUI::Registrar::ConversationServerState'
        '!Microsoft::CoreUI::Registrar::RegisteredObjectType'
        '!Microsoft::CoreUI::Registrar::RegistrarClient__ServerConnectionState'
        '!Microsoft::CoreUI::Registrar::ServiceRunMode'
        '!Microsoft::CoreUI::RegistrarScope'
        '!Microsoft::CoreUI::Resources::ResourceType'
        '!Microsoft::CoreUI::RestartPolicy'
        '!Microsoft::CoreUI::Support::SkipList__SearchMode'
        '!Microsoft::CoreUI::Support::SkipMode'
        '!Microsoft::CoreUI::Support::Win32EventType'
        '!Microsoft::CoreUI::ThreadID'
        '!Microsoft::CoreUI::ValidationException__GeneralErrors'
        '!Microsoft::CoreUI::WaitFlags'
        '!Microsoft::CoreUI::WaitRegistration'
        '!Microsoft::CoreUI::WindowID'
        '!Microsoft::CoreUI::WindowIDInfoFlags'
        '!Microsoft::CoreUI::WindowIDProperty'
        '!System::AttributeTargets'
        '!System::DateTimeKind'
        '!System::DayOfWeek'
        '!System::Diagnostics::DebuggerBrowsableState'
        '!System::Globalization::NumberStyles'
        '!System::IO::SeekOrigin'
        '!System::Reflection::BindingFlags'
        '!System::Runtime::InteropServices::CharSet'
        '!System::Runtime::InteropServices::GCHandleType'
        '!System::Runtime::InteropServices::LayoutKind'
        '!System::Runtime::InteropServices::UnmanagedType'
        '!System::StringComparison'
        '!System::Threading::ThreadState'
     ) -Converter {

        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()
            return $stockValue.raw__
        }
        finally { }
    } # end CN enum converter
}

