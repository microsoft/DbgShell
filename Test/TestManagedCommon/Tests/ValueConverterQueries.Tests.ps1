
Describe "ValueConverterQueries" {

    pushd

    It "can query for symbol value converters" {

        $converterInfos = [object[]] (Get-DbgValueConverterInfo)
        0 | Should Not Be $converterInfos.Count

        $converterInfos = [object[]] (Get-DbgValueConverterInfo 'std::vector<?*>')
        2 | Should Be $converterInfos.Count

        $converterInfos = [object[]] (Get-DbgValueConverterInfo 'std::vector<bool>')
        2 | Should Be $converterInfos.Count

        $converterInfos = [object[]] (Get-DbgValueConverterInfo 'std::vector<int>')
        1 | Should Be $converterInfos.Count

        $converterInfos = [object[]] (Get-DbgValueConverterInfo 'std::vector<int, blah>')
        1 | Should Be $converterInfos.Count

        $converterInfos = [object[]] (Get-DbgValueConverterInfo 'std::vector<int, blah>' -ExactMatch)
        $converterInfos | Should BeNullOrEmpty

        $converterInfos = [object[]] (Get-DbgValueConverterInfo 'std::vector<?*>' -ExactMatch)
        1 | Should Be $converterInfos.Count
        ([string]::IsNullOrEmpty( $converterInfos[ 0 ].ScopingModule )) | Should Be $true


        [bool] $itThrew = $false
        try
        {
            $converterInfos = [object[]] (Get-DbgValueConverterInfo -ExactMatch -ErrorAction Stop)
        }
        catch
        {
            $itThrew = $true
        }

        $itThrew | Should Be $true
    }

    popd
}

