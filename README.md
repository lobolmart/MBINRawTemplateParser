# MBINRawTemplateParser 

this is an experimental parser that takes an IDA decompiled output (hexrays)
for a certain NMS subroutine where a raw MBIN struct is populated with it's default values.

the sole purpose of the tool is to convert the struct into a readable
MBINCompiler struct:
https://github.com/emoose/MBINCompiler

pretty much only tested with GcUIGlobals at this point. check this gist
for an example of how the input looks like:
https://gist.github.com/lobolmart/d959a96166b033d153f42faae19b103c

the output:
https://gist.github.com/lobolmart/d2dfe1d7cb1391d15557fd2dd91be803

please, note that this tool:
- might make mistakes when guessing the types of certain properties
- has missing parser features and the user will need to double check and most
likely edit the output
- can only be used to define the raw MBIN struct and cannot be used to guess
the names of properties
