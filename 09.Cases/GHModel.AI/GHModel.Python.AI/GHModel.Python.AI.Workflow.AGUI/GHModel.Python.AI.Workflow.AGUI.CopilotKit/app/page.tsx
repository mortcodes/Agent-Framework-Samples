"use client";
import React, { useState } from "react";
import "./styles.css";
import { useFrontendTool } from "@copilotkit/react-core";
import { CopilotChat } from "@copilotkit/react-ui";

interface AgenticChatProps {
  params: Promise<{
    integrationId: string;
  }>;
}

const AgenticChat: React.FC<AgenticChatProps> = ({ params }) => {
  const { integrationId } = React.use(params);

  return <Chat />;
};

const Chat = () => {
  const [background, setBackground] = useState<string>("--copilot-kit-background-color");

  useFrontendTool({
    name: "change_background",
    description:
      "Change the background color of the chat. Can be anything that the CSS background attribute accepts. Regular colors, linear of radial gradients etc.",
    parameters: [
      {
        name: "background",
        type: "string",
        description: "The background. Prefer gradients. Only use when asked.",
      },
    ],
    handler: ({ background }) => {
      setBackground(background);
      return {
        status: "success",
        message: `Background changed to ${background}`,
      };
    },
  });

  return (
    <div
      className="flex justify-center items-center w-full min-h-screen"
      data-testid="background-container"
      style={{ background, minHeight: "100vh" }}
    >
      <div
        className="rounded-lg"
        style={{ width: "98vw", height: "100vh", maxWidth: "1800px" }}
      >
        <CopilotChat
          className="h-full w-full rounded-2xl"
          labels={{ initial: "Hi, I'm your game design assistant. How can I assist you today?" }}
        />
      </div>
    </div>
  );
};

export default AgenticChat;