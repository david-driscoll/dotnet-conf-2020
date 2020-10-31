/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { connect } from "net";
import * as path from "path";
import { resolve } from "path";
import { workspace } from "vscode";
import { ExtensionContext } from "vscode";

import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  StreamInfo,
  TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient;

export function activate(context: ExtensionContext) {
  const connectFunc = () => {
    return new Promise<StreamInfo>((resolve, reject) => {
      // TODO: Demoware - Named pipes are handled differently by differently on *NIX
      // https://github.com/PowerShell/PowerShellEditorServices/blob/65a7a79e7f17d76e232b43507ca58a14d79e64eb/src/PowerShellEditorServices.Hosting/Internal/NamedPipeUtils.cs#L142
      const socket = connect(`\\\\.\\pipe\\ninrocks`);
      socket.on("connect", () => {
        resolve({ writer: socket, reader: socket });
      });
    });
  };

  client = new LanguageClient("nin", "NIN", connectFunc, {
    documentSelector: [
      {
        language: "ini",
      },
      {
        language: "nin",
      },
      {
        pattern: "**/*.ini",
      },
      {
        pattern: "**/*.nin",
      },
    ],

    progressOnInitialization: true,
    synchronize: {
      // Synchronize the setting section 'languageServerExample' to the server
      // configurationSection: "languageServerExample",
      // fileEvents: workspace.createFileSystemWatcher("**/*.cs"),
    },
  });
  client.registerProposedFeatures();
  client.start();
}

export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}
