
Describe "TemplateMatching" {
    It "can crack string templates" {

        $typeName1 = "std::basic_string<unsigned short,std::char_traits<unsigned short>,std::allocator<unsigned short>,_STL70>"
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti2 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::basic_string<?*>' )
        $ti1.Matches( $ti1 ) | Should Be $true
        $ti1.Matches( $ti2 ) | Should Be $true
        $ti2.Matches( $ti1 ) | Should Be $true

        $ti3 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::basic_string2<?*>' )
        $ti1.Matches( $ti3 ) | Should Be $false
        $ti3.Matches( $ti2 ) | Should Be $false

        $ti4 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::basic_string<?,std::char_traits<?>,?,_STL70>' )
        $ti1.Matches( $ti4 ) | Should Be $true
        $ti4.Matches( $ti1 ) | Should Be $true
        $ti4.Matches( $ti4 ) | Should Be $true
    }

    It "can deal with weird spacing" {

        # Notice that this template name has some odd spacing.
        $typeName2 = "std::vector<Config::ReplicaSet *,std::allocator<Config::ReplicaSet *> >"
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName2 )
        $ti2 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::vector<?*>' )
        $ti1.Matches( $ti1 ) | Should Be $true
        $ti1.Matches( $ti2 ) | Should Be $true
        $ti2.Matches( $ti1 ) | Should Be $true

        $ti3 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::vector<Config::ReplicaSet*, std::allocator<Config::ReplicaSet*>>' )
        $ti1.Matches( $ti3 ) | Should Be $true
        $ti3.Matches( $ti1 ) | Should Be $true
        $ti3.Matches( $ti3 ) | Should Be $true
    }

    It "can deal with nested types" {

        $typeName1 = 'std::list<TRANS_ENTRY,std::allocator<TRANS_ENTRY> >::iterator'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti2 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::list<?*>' )
        $ti1.Matches( $ti1 ) | Should Be $true
        $ti1.Matches( $ti2 ) | Should Be $false
        $ti2.Matches( $ti1 ) | Should Be $false
        $ti3 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::list<?*>::iterator' )
        $ti3.Matches( $ti3 ) | Should Be $true
        $ti1.Matches( $ti3 ) | Should Be $true
        $ti3.Matches( $ti1 ) | Should Be $true
        $ti3.Matches( $ti2 ) | Should Be $false
        $ti2.Matches( $ti3 ) | Should Be $false


        $typeName1 = 'std::list<TRANS_ENTRY,std::allocator<TRANS_ENTRY> >::blah<int, 5>'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.Matches( $ti2 ) | Should Be $false
        $ti2.Matches( $ti1 ) | Should Be $false
        $ti2 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::list<?*>::blah' )
        $ti1.Matches( $ti2 ) | Should Be $false
        $ti2.Matches( $ti1 ) | Should Be $false
        $ti2 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::list<?*>::blah<int, ?>' )
        $ti1.Matches( $ti2 ) | Should Be $true
        $ti2.Matches( $ti1 ) | Should Be $true
    }

    It "can deal with special non-template names" {

        # Special non-template name.
        $unnamedTagName = '<unnamed-tag>'
        $typeName1 = $unnamedTagName
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $false
        $ti1.FullName | Should Be $unnamedTagName
        $ti1.TemplateName | Should Be $unnamedTagName
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $unnamedTagName )) | Should Be $false
        $ti1.HasConst | Should Be $false

        $unnamedTagName = '_ULARGE_INTEGER::<unnamed-type-u>'
        $typeName1 = $unnamedTagName
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $false
        $ti1.FullName | Should Be $unnamedTagName
        $ti1.TemplateName | Should Be $unnamedTagName
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $unnamedTagName )) | Should Be $false
        $ti1.HasConst | Should Be $false

        $unnamedTagName = '_ULARGE_INTEGER::<unnamed-type-u>::foo<blah>'
        $typeName1 = $unnamedTagName
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be $unnamedTagName
        $ti1.TemplateName | Should Be '_ULARGE_INTEGER::<unnamed-type-u>::foo'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $unnamedTagName )) | Should Be $true
        $ti1.HasConst | Should Be $false
    }

    It "can deal with short names" {

        $typeName1 = 'A<B>'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be $typeName1
        $ti1.TemplateName | Should Be 'A'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $true
        $ti1.HasConst | Should Be $false

        $typeName1 = 'A<B>::C'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be $typeName1
        $ti1.TemplateName | Should Be 'A'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $true
        $ti1.HasConst | Should Be $false
        $ti1.NestedNode -ne $null | Should Be $true
        $ti1.NestedNode.TemplateName | Should Be 'C'
        $ti1.NestedNode.IsTemplate | Should Be $false
    }

    It "can deal with function types" {

        $typeName1 = 'FrsStatus* (*fn)( Db*, DbIterator<ID_RECORD>*, ID_RECORD*, Int4B* )'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $false
        $ti1.FullName | Should Be $typeName1
        $ti1.TemplateName | Should Be $typeName1
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $false
        $ti1.HasConst | Should Be $false
    }

    It "can deal with anonymous namespace thing" {

        $typeName1 = 'VolatilePtr<`anonymous namespace''::AptcaKillBitList,A0x8f085d03::AptcaKillBitList *>'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be 'VolatilePtr<`anonymous namespace''::AptcaKillBitList,A0x8f085d03::AptcaKillBitList*>'
        $ti1.TemplateName | Should Be 'VolatilePtr'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $true
        $ti1.HasConst | Should Be $false
    }

    It "can deal with const" {

        $typeName1 = 'std::pair<VolumeId,FileId> const '
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be 'std::pair<VolumeId,FileId>' # We trim off "const"
        $ti1.HasConst | Should Be $true
        $ti1.TemplateName | Should Be 'std::pair'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $true

        $typeName1 = 'std::map<std::pair<VolumeId,FileId>,std::pair<Staging *,unsigned long>,std::less<std::pair<VolumeId,FileId> >,std::allocator<std::pair<std::pair<VolumeId,FileId> const ,std::pair<Staging *,unsigned long> > > >'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be 'std::map<std::pair<VolumeId,FileId>,std::pair<Staging*,unsigned long>,std::less<std::pair<VolumeId,FileId>>,std::allocator<std::pair<std::pair<VolumeId,FileId>,std::pair<Staging*,unsigned long>>>>'
        $ti1.TemplateName | Should Be 'std::map'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $true
        $ti1.HasConst | Should Be $false
    }

    It "can deal with references" {

        $typeName1 = 'std::pair<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > const ,std::function<int __cdecl(std::vector<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> >,std::allocator<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > > > &)> > const &'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $false # because references are not templates (they're just like pointers)
        $ti1.FullName | Should Be 'std::pair<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > const ,std::function<int __cdecl(std::vector<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> >,std::allocator<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > > > &)> > const &'
        $ti1.TemplateName | Should Be 'std::pair<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > const ,std::function<int __cdecl(std::vector<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> >,std::allocator<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > > > &)> > const &'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $false
        $ti1.HasConst | Should Be $false # it looks like it has const, but HasConst is only for actual templates

        $typeName1 = 'std::vector<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> >,std::allocator<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > > > &'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $false # because references are not templates (they're just like pointers)
        $ti1.FullName | Should Be 'std::vector<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> >,std::allocator<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > > > &'
        $ti1.TemplateName | Should Be 'std::vector<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> >,std::allocator<std::basic_string<wchar_t,std::char_traits<wchar_t>,std::allocator<wchar_t> > > > &'
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $false
        $ti1.HasConst | Should Be $false # it looks like it has const, but HasConst is only for actual templates
    }

    It "can deal with lambdas" {

        $typeName1 = 'Concurrency::details::__I?$_AsyncTaskGeneratorThunk@V<lambda_fcd0511faf603cca0b560671c4e52d53>@@PublicNonVirtuals'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $false # the lambda thing is not a template
        $ti1.FullName | Should Be $typeName1
        $ti1.TemplateName | Should Be $typeName1
        ([MS.Dbg.DbgTemplateNode]::LooksLikeATemplateName( $typeName1 )) | Should Be $false

        $typeName1 = 'Concurrency::details::_AsyncTaskGeneratorThunk<<lambda_fcd0511faf603cca0b560671c4e52d53> >'
        $ti1 = [MS.Dbg.DbgTemplateNode]::CrackTemplate( $typeName1 )
        $ti1.IsTemplate | Should Be $true
        $ti1.FullName | Should Be 'Concurrency::details::_AsyncTaskGeneratorThunk<<lambda_fcd0511faf603cca0b560671c4e52d53>>'
        $ti1.TemplateName | Should Be 'Concurrency::details::_AsyncTaskGeneratorThunk'
        1 | Should Be $ti1.Parameters.Count
        $ti1.Parameters[ 0 ].IsTemplate | Should Be $false # the lambda thing inside is not a template
        $ti1.Parameters[ 0 ].FullName | Should Be '<lambda_fcd0511faf603cca0b560671c4e52d53>'
    }

    It "throws if the multi-match wildcard doesn't come last" {

        [bool] $itThrew = $false
        try
        {
            # Can't use the multi-match wildcard ('?*') unless it comes last.
            $tiBad = [MS.Dbg.DbgTemplateNode]::CrackTemplate( 'std::basic_string<?*,std::char_traits<?>,?,_STL70>' )
        }
        catch
        {
            $itThrew = $true
        }

        $itThrew | Should Be $true
    }
}

