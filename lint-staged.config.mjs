import { relative } from 'node:path';

export default {
  '*.cs': (files) => `dotnet format Bepthaymao.slnx --verify-no-changes --include ${files
    .map((file) => `"${relative(process.cwd(), file)}"`).join(' ')}`,
};
