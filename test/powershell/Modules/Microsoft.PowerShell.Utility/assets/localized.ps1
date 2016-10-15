# Sample code from https://technet.microsoft.com/en-us/library/hh847854.aspx

    $Day = DATA {
# culture="en-US"
ConvertFrom-StringData @'
    messageDate = Today is
    d0 = Sunday
    d1 = Monday
    d2 = Tuesday
    d3 = Wednesday
    d4 = Thursday
    d5 = Friday
    d6 = Saturday
'@
}


Import-LocalizedData -BindingVariable Day

# Build an array of weekdays.
$a = $Day.d0, $Day.d1, $Day.d2, $Day.d3, $Day.d4, $Day.d5, $Day.d6


        # Get the day of the week as a number (Monday = 1).
        # Index into $a to get the name of the day.
        # Use string formatting to build a sentence.

        "{0} {1}" -f $Day.messageDate, $a[(get-date -uformat %u)] | Out-Host

